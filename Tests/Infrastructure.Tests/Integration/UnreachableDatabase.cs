using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Infrastructure.Tests.Integration;

internal static class UnreachableDatabase
{
    /// <summary>Nothing listens on that port: the connection is refused, as with a database that is not up.</summary>
    public const string ConnectionString = "Host=127.0.0.1;Port=1;Database=Nothing;Username=nobody;Password=nothing;SSL Mode=Disable";
}

/// <summary>A server that takes the connection and says nothing: without a limit of its own, the answer would depend on the network.</summary>
internal sealed class SilentDatabase : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);

    public SilentDatabase() => _listener.Start();

    public string ConnectionString => string.Create(
        CultureInfo.InvariantCulture,
        $"Host=127.0.0.1;Port={((IPEndPoint)_listener.LocalEndpoint).Port};Database=Nothing;Username=nobody;Password=nothing;SSL Mode=Disable");

    /// <summary>The connections it has received since the last time it was asked.</summary>
    public async Task<int> TakeConnectionsAsync()
    {
        var connections = 0;
        while (_listener.Pending())
        {
            (await _listener.AcceptTcpClientAsync()).Dispose();
            connections++;
        }

        return connections;
    }

    public void Dispose() => _listener.Dispose();
}
