using System.Security.Cryptography;
using System.Text.RegularExpressions;
using DispatchGuard.API.Perimeter;
using DispatchGuard.API.Session;
using DispatchGuard.Core.Auditing;
using DispatchGuard.Core.Interfaces;

namespace DispatchGuard.API.Auditing;

/// <summary>
/// The thread of a request (SPEC 0085 R1): the <c>traceparent</c> of the web, where all that follows an action shares its id. Read
/// here and not from <c>Activity</c>, which depends on who is listening.
/// </summary>
internal sealed partial record RequestThread(string TraceId, bool FollowsAnAction)
{
    public const string Header = "traceparent";

    private static readonly object Key = new();

    public static RequestThread Of(HttpContext context)
    {
        if (context.Items[Key] is not RequestThread thread)
        {
            context.Items[Key] = thread = Parse(context.Request.Headers[Header].ToString());
        }

        return thread;
    }

    public static bool IsTraceId(string? text) => text is not null && TraceIdFormat().IsMatch(text);

    private static RequestThread Parse(string header)
    {
        var match = Format().Match(header);
        return match.Success
            ? new RequestThread(match.Groups["trace"].Value, match.Groups["flags"].Value == "01")
            : new RequestThread(Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(RequestTrace.TraceIdLength / 2)), FollowsAnAction: true);
    }

    [GeneratedRegex(@"\A00-(?<trace>[0-9a-f]{32})-[0-9a-f]{16}-(?<flags>[0-9a-f]{2})\z")]
    private static partial Regex Format();

    [GeneratedRegex(@"\A[0-9a-f]{32}\z")]
    private static partial Regex TraceIdFormat();
}

public sealed class RequestOrigin(IHttpContextAccessor accessor, DemoResetKey resetKey, CronSecret cronSecret) : IAuditOrigin
{
    /// <summary>Percent-encoded: a header only admits ASCII.</summary>
    public const string NameHeader = "X-DispatchGuard-Name";

    /// <summary>A page opened before the rename still sends it (SPEC 0094 R3).</summary>
    public const string PreviousNameHeader = "X-LOF-Name";

    private HttpContext? Context => accessor.HttpContext;

    public string? SessionId => AnonymousSession.IdOf(Context?.User);

    public string? TraceId => Context is null ? null : RequestThread.Of(Context).TraceId;

    public string? DeclaredName
    {
        get
        {
            var encoded = Context is { } context ? EncodedName(context.Request.Headers) : string.Empty;
            var name = new string([.. Uri.UnescapeDataString(encoded).Where(character => !char.IsControl(character) && !char.IsSurrogate(character))]).Trim();
            return name.Length == 0 ? null : name[..Math.Min(name.Length, AuditEvent.DeclaredNameMaxLength)];
        }
    }

    private static string EncodedName(IHeaderDictionary headers) =>
        (headers.TryGetValue(NameHeader, out var name) ? name : headers[PreviousNameHeader]).ToString();

    public bool IsDemoLoad => Context is { } context && (resetKey.IsIn(context.Request) || cronSecret.IsIn(context.Request));
}
