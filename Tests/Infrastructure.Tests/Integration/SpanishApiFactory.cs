using Microsoft.AspNetCore.Mvc.Testing;

namespace Infrastructure.Tests.Integration;

/// <summary>
/// The API as a Spanish reader sees it: most tests read its texts, and they prove that the Spanish ones did not change (SPEC 0095).
/// The other language and the default one have tests of their own (<see cref="LanguageHttpTests"/>).
/// </summary>
public sealed class SpanishApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureClient(HttpClient client)
    {
        base.ConfigureClient(client);
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("es");
    }
}
