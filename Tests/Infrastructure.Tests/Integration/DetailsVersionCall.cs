using System.Net.Http.Json;
using System.Text.Json;

namespace Infrastructure.Tests.Integration;

internal static class DetailsVersionCall
{
    /// <summary>The one that a driver or a vehicle has now: changing its data requires it (SPEC 0090).</summary>
    public static async Task<string> DetailsVersionAsync(this HttpClient client, string url) =>
        (await client.GetFromJsonAsync<JsonElement>(url)).GetProperty("DetailsVersion").GetString()!;
}
