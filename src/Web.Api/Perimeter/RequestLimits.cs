using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Threading.RateLimiting;
using DispatchGuard.API.Http;
using DispatchGuard.API.Session;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DispatchGuard.API.Perimeter;

/// <summary>
/// A request needs room in every limit that applies to it (SPEC 0089, ADR 0022). Those of the session go first: what one browser
/// wastes is not taken from those that share its address.
/// </summary>
public sealed class RequestLimits(RequestLimitSettings settings, DemoResetKey resetKey, CronSecret cronSecret, bool loopbackIsExempt)
{
    private const string Exempt = "exempt";

    /// <summary>Shared by every request whose origin is not known: none of them is left without a limit.</summary>
    private const string UnknownOrigin = "unknown";

    private const int Ipv6BytesOfTheNetwork = 8;

    public void Configure(RateLimiterOptions options)
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        options.OnRejected = WriteRejectionAsync;
        options.GlobalLimiter = PartitionedRateLimiter.CreateChained(Limiter(OfSession), Limiter(OfCostlyRequest), Limiter(OfIp));
    }

    private PartitionedRateLimiter<HttpContext> Limiter(Func<HttpContext, RateLimitPartition<string>> partitionOf) =>
        PartitionedRateLimiter.Create<HttpContext, string>(
            context => IsExempt(context) ? RateLimitPartition.GetNoLimiter(Exempt) : partitionOf(context));

    private bool IsExempt(HttpContext context) =>
        (loopbackIsExempt && context.Connection.RemoteIpAddress is var address && (address is null || IPAddress.IsLoopback(address)))
        || resetKey.IsIn(context.Request)
        || cronSecret.IsIn(context.Request);

    private RateLimitPartition<string> OfSession(HttpContext context)
    {
        if (AnonymousSession.IdOf(context.User) is not { } session)
        {
            return RateLimitPartition.GetNoLimiter(Exempt);
        }

        return context.IsHeavyCalculation() ? Fixed($"heavy {session}", settings.Session.Heavy)
            : context.RecordsVisit() ? Fixed($"visit {session}", settings.Session.VisitBatches)
            : context.Request.IsRead() ? Fixed($"read {session}", settings.Session.Reads)
            : Fixed($"write {session}", settings.Session.Writes);
    }

    private RateLimitPartition<string> OfCostlyRequest(HttpContext context)
    {
        if (context.IsHeavyCalculation())
        {
            return Fixed($"heavy {OriginOf(context)}", settings.Ip.Heavy);
        }

        return context.StartsSession() && AnonymousSession.IdOf(context.User) is null
            ? Fixed($"session {OriginOf(context)}", settings.Ip.NewSessions)
            : RateLimitPartition.GetNoLimiter(Exempt);
    }

    private RateLimitPartition<string> OfIp(HttpContext context) => Fixed(OriginOf(context), settings.Ip.Total);

    private RateLimitPartition<string> Fixed(string key, int limit) => RateLimitPartition.GetFixedWindowLimiter(
        key, _ => new FixedWindowRateLimiterOptions { PermitLimit = limit, Window = settings.Window, QueueLimit = 0 });

    private static string OriginOf(HttpContext context) => context.Connection.RemoteIpAddress switch
    {
        null => UnknownOrigin,
        { IsIPv4MappedToIPv6: true } address => address.MapToIPv4().ToString(),
        { AddressFamily: AddressFamily.InterNetworkV6 } address => NetworkOf(address),
        var address => address.ToString()
    };

    /// <summary>The /64: whoever has an IPv6 address has the 2^64 of its network to change to.</summary>
    private static string NetworkOf(IPAddress address) => Convert.ToHexString(address.GetAddressBytes().AsSpan(0, Ipv6BytesOfTheNetwork));

    private static async ValueTask WriteRejectionAsync(OnRejectedContext rejection, CancellationToken cancellationToken)
    {
        if (rejection.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait))
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(wait.TotalSeconds));
            rejection.HttpContext.Response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        var problems = rejection.HttpContext.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problems.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = rejection.HttpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status429TooManyRequests,
                Title = Texts.TooManyRequestsTitle().In(CurrentLanguage.Value),
                Detail = Texts.TooManyRequests().In(CurrentLanguage.Value)
            }
        });
    }
}
