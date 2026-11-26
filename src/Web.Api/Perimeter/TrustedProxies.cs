using Microsoft.AspNetCore.HttpOverrides;
using IPNetwork = System.Net.IPNetwork;

namespace DispatchGuard.API.Perimeter;

/// <summary>Who may say, through <c>X-Forwarded-*</c>, where a request really comes from (SPEC 0089).</summary>
public static class TrustedProxies
{
    private const string Setting = "ForwardedHeaders:TrustedNetworks";

    /// <exception cref="InvalidOperationException">A network is not one, or trusts every address.</exception>
    public static IReadOnlyList<IPNetwork> From(IConfiguration configuration) =>
        (configuration.GetSection(Setting).Get<string[]>() ?? []).Select(Parse).ToList();

    /// <summary>
    /// Always called, also without networks: a host that sets <c>ASPNETCORE_FORWARDEDHEADERS_ENABLED</c> would otherwise trust
    /// anyone, and any visitor could choose its own quota.
    /// </summary>
    public static void Configure(ForwardedHeadersOptions options, IReadOnlyList<IPNetwork> networks)
    {
        options.ForwardedHeaders = networks.Count == 0
            ? ForwardedHeaders.None
            : ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        // The last hop is the one the proxy wrote; the previous ones come from the client.
        options.ForwardLimit = 1;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var network in networks) options.KnownIPNetworks.Add(network);
    }

    private static IPNetwork Parse(string text) =>
        IPNetwork.TryParse(text, out var network) && network.PrefixLength > 0
            ? network
            : throw new InvalidOperationException($"{Setting}: '{text}' is not a network, or trusts every address");
}
