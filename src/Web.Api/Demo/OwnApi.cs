using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Net.Http.Headers;

namespace DispatchGuard.API.Demo;

/// <summary>
/// This same process, by HTTP and without leaving the machine: through the public address another instance would answer, and the
/// firewall of the host would take it for a bot.
/// </summary>
public sealed class OwnApi(IServer server)
{
    public HttpClient CreateClient(HttpRequest caller)
    {
        var handler = new VisitorHandler(caller.Host.Value!, caller.Headers.Authorization.ToString());
        return new HttpClient(handler) { BaseAddress = Address() };
    }

    private Uri Address()
    {
        var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses ?? [];
        var plain = addresses.Select(BindingAddress.Parse).FirstOrDefault(address => address.Scheme == Uri.UriSchemeHttp)
            ?? throw new InvalidOperationException("The API does not listen in plain HTTP");
        return new Uri($"{Uri.UriSchemeHttp}://127.0.0.1:{plain.Port}");
    }

    /// <summary>By hand, because a cookie container would not send a secure cookie without HTTPS.</summary>
    private sealed class VisitorHandler(string host, string authorization)
        : DelegatingHandler(new SocketsHttpHandler { UseCookies = false, UseProxy = false, AllowAutoRedirect = false })
    {
        private string? _cookie;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.Headers.Host = host;
            request.Headers.TryAddWithoutValidation(HeaderNames.Authorization, authorization);
            if (_cookie is not null)
            {
                request.Headers.Add(HeaderNames.Cookie, _cookie);
            }

            var response = await base.SendAsync(request, cancellationToken);
            if (response.Headers.TryGetValues(HeaderNames.SetCookie, out var cookies))
            {
                _cookie = cookies.Single().Split(';')[0];
            }

            return response;
        }
    }
}
