using System.Security.Claims;
using System.Security.Cryptography;
using DispatchGuard.Core.Entities;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace DispatchGuard.API.Session;

/// <summary>The session of a browser, kept in an encrypted and signed cookie: only the server can have written its id.</summary>
public static class AnonymousSession
{
    public const string Scheme = "AnonymousSession";

    /// <summary>What others may see of it: enough to tell sessions apart.</summary>
    public const int ShortIdLength = AllocationAttempt.ShortSessionIdLength;

    private const string IdClaim = "lof:session";

    private const string SecureCookieName = "__Host-lof-session";

    /// <summary>Browsers refuse the <c>__Host-</c> prefix in a cookie that is not secure.</summary>
    private const string PlainCookieName = "lof-session";

    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public static void Configure(CookieAuthenticationOptions options, SessionSettings settings)
    {
        options.Cookie.Name = settings.AllowsPlainHttp ? PlainCookieName : SecureCookieName;
        options.Cookie.SecurePolicy = settings.AllowsPlainHttp ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.Path = "/";
        options.Cookie.IsEssential = true;
        options.ExpireTimeSpan = Lifetime;
        options.SlidingExpiration = true;
    }

    public static string? IdOf(ClaimsPrincipal? user) => user?.FindFirstValue(IdClaim);

    public static string ShortIdOf(string id) => id[..ShortIdLength];

    public static async Task<string> StartAsync(HttpContext context)
    {
        var id = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(AllocationAttempt.SessionIdLength / 2));
        var identity = new ClaimsIdentity([new Claim(IdClaim, id)], Scheme);
        await context.SignInAsync(Scheme, new ClaimsPrincipal(identity), new AuthenticationProperties { IsPersistent = true });
        return id;
    }
}
