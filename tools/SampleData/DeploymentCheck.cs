using System.Globalization;
using System.Net;

namespace DispatchGuard.SampleData;

public sealed record CheckResult(string Name, bool Passed, string Detail);

/// <summary>
/// What an installation of the public demo must answer (SPEC 0091). It changes nothing, not even where it finds something wrong:
/// what it asks to delete does not exist.
/// </summary>
public sealed class DeploymentCheck(HttpClient http)
{
    private const string WriteHeader = "X-DispatchGuard-Request";

    private static readonly string Unknown = int.MaxValue.ToString(CultureInfo.InvariantCulture);

    public async Task<IReadOnlyList<CheckResult>> RunAsync(CancellationToken cancellationToken = default)
    {
        var results = new List<CheckResult>
        {
            await ExpectAsync("The process answers", HttpMethod.Get, "/health/live", HttpStatusCode.OK, cancellationToken),
            await ExpectAsync("The web is served", HttpMethod.Get, "/", HttpStatusCode.OK, cancellationToken, "text/html"),
            // Before the session starts: from then on the client carries its cookie.
            await ExpectAsync("Reading needs a session", HttpMethod.Get, "/health/ready", HttpStatusCode.Unauthorized, cancellationToken),
            await ExpectAsync("Writing needs a session", HttpMethod.Delete, $"/api/bookings/{Unknown}", HttpStatusCode.Unauthorized, cancellationToken)
        };
        results.Add(await ExpectAsync(
            "Rebuilding the demo needs its secret", HttpMethod.Get, "/api/admin/demo/rebuild", HttpStatusCode.Unauthorized, cancellationToken));

        await StartSessionAsync(cancellationToken);
        results.Add(await ExpectAsync("The database answers", HttpMethod.Get, "/health/ready", HttpStatusCode.OK, cancellationToken));
        foreach (var resource in new[] { "drivers", "vehicles", "locations" })
        {
            results.Add(await ExpectAsync(
                $"Deletion is locked: {resource}", HttpMethod.Delete, $"/api/{resource}/{Unknown}", HttpStatusCode.Forbidden, cancellationToken));
        }

        return results;
    }

    /// <summary>If it fails, what needs the session says so.</summary>
    private Task<CheckResult> StartSessionAsync(CancellationToken cancellationToken) =>
        ExpectAsync("Session", HttpMethod.Get, "/api/session", HttpStatusCode.OK, cancellationToken);

    private async Task<CheckResult> ExpectAsync(
        string name, HttpMethod method, string url, HttpStatusCode expected, CancellationToken cancellationToken, string? mediaType = null)
    {
        try
        {
            using var request = new HttpRequestMessage(method, url);
            request.Headers.Add(WriteHeader, "1");
            using var response = await http.SendAsync(request, cancellationToken);
            var type = response.Content.Headers.ContentType?.MediaType;
            return new CheckResult(
                name,
                response.StatusCode == expected && (mediaType is null || mediaType == type),
                $"{method} {url} → {(int)response.StatusCode} {type}, expected {(int)expected} {mediaType}".TrimEnd());
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new CheckResult(name, false, $"{method} {url} → {exception.Message}");
        }
    }
}
