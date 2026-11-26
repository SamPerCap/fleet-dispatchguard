using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;

namespace DispatchGuard.API.XRay;

/// <summary>
/// The X-ray travels sealed to the browser that asked (SPEC 0097 R1): encrypted and signed with the session key, with the session
/// and the instant inside. The server keeps nothing, so every instance can read it and nobody can list or guess one.
/// </summary>
public sealed class XRayTokens(IDataProtectionProvider protection, TimeProvider clock)
{
    public const string Header = "X-DispatchGuard-XRay";

    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>A sealed X-ray is a few kilobytes; anything much longer was not written here.</summary>
    private const int LongestToken = 16_384;

    private readonly IDataProtector _protector = protection.CreateProtector("DispatchGuard.XRay.v1");

    private sealed record Sealed(string Session, DateTimeOffset IssuedAt, XRayDto XRay);

    public string Issue(string session, XRayDto xray) => Issue(session, xray, clock.GetUtcNow());

    public string Issue(string session, XRayDto xray, DateTimeOffset issuedAt) =>
        WebEncoders.Base64UrlEncode(_protector.Protect(JsonSerializer.SerializeToUtf8Bytes(new Sealed(session, issuedAt, xray))));

    /// <returns>Null for anything but a recent X-ray of <paramref name="session"/>: whoever asks cannot tell why.</returns>
    public XRayDto? Read(string? token, string session)
    {
        if (string.IsNullOrEmpty(token) || token.Length > LongestToken) return null;
        Sealed? opened;
        try
        {
            opened = JsonSerializer.Deserialize<Sealed>(_protector.Unprotect(WebEncoders.Base64UrlDecode(token)));
        }
        catch (Exception exception) when (exception is CryptographicException or FormatException or JsonException)
        {
            return null;
        }

        if (opened is null) return null;
        var age = clock.GetUtcNow() - opened.IssuedAt;
        return SameSession(opened.Session, session) && age >= TimeSpan.Zero && age <= Lifetime ? opened.XRay : null;
    }

    private static bool SameSession(string sealedFor, string asking) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(sealedFor), Encoding.UTF8.GetBytes(asking));
}
