using System.Net.Sockets;

namespace DispatchGuard.API.Perimeter;

/// <summary>Who a request comes from, as the perimeter counts it (SPEC 0089): the real address after the proxy headers.</summary>
public static class ClientOrigin
{
    /// <summary>Shared by every request whose origin is not known: none of them is left without a limit.</summary>
    private const string UnknownOrigin = "unknown";

    private const int Ipv6BytesOfTheNetwork = 8;

    public static string Of(HttpContext context) => context.Connection.RemoteIpAddress switch
    {
        null => UnknownOrigin,
        { IsIPv4MappedToIPv6: true } address => address.MapToIPv4().ToString(),
        { AddressFamily: AddressFamily.InterNetworkV6 } address => NetworkOf(address),
        var address => address.ToString()
    };

    /// <summary>The /64: whoever has an IPv6 address has the 2^64 of its network to change to.</summary>
    private static string NetworkOf(System.Net.IPAddress address) =>
        Convert.ToHexString(address.GetAddressBytes().AsSpan(0, Ipv6BytesOfTheNetwork));
}
