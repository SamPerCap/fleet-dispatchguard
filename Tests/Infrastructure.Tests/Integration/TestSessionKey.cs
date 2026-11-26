using Microsoft.AspNetCore.Hosting;

namespace Infrastructure.Tests.Integration;

/// <summary>Without a key the API only starts in Development and Testing (SPEC 0086).</summary>
internal static class TestSessionKey
{
    public const string Setting = "Session:Key";

    public const string Value = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    public static IWebHostBuilder UseSessionKey(this IWebHostBuilder builder) => builder.UseSetting(Setting, Value);
}
