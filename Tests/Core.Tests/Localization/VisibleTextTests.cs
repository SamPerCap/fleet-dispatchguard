using System.Text.RegularExpressions;

namespace Core.Tests.Localization;

/// <summary>
/// No text for people outside the catalogue (SPEC 0095 CA2). Types do most of it, as the DTOs and <c>ProblemDetails</c> only take
/// texts of the catalogue; this looks for what they cannot see.
/// </summary>
public class VisibleTextTests
{
    /// <summary>Only the author reads them, in Spanish (SPEC 0095 R7), or they are stored and read by the web as a code.</summary>
    private static readonly string[] Allowed =
    [
        "Web.Api/Auditing/VisitNotice.cs",
        "Core/Entities/AllocationAttempt.cs",
        "Core/Entities/Booking.cs"
    ];

    private static readonly string Source = FindSource();

    [Fact]
    public void Source_HasNoSpanishOutsideTheCatalogue()
    {
        var spanish = new Regex("""
            "[^"]*([áéíóúñÁÉÍÓÚÑ¿¡«»]|\b(el|la|los|las|del|para|con|sin|servicio|conductor|vehículo|reserva)\b )[^"]*"
            """.Trim());

        // The characters that a pattern admits are not a text.
        Assert.Empty(Lines().Where(line => spanish.IsMatch(line.Text) && !line.Text.Contains("RegularExpression(", StringComparison.Ordinal)).Select(line => line.Where));
    }

    [Fact]
    public void Problems_AndRequestErrors_ComeFromTheCatalogue()
    {
        var literal = new Regex("""(?:[Tt]itle|[Dd]etail)\s*[:=]\s*\$?"|ErrorMessage\s*=\s*"|new InvalidRequestException\(\s*\$?"|new ResourceNotFoundException\(\s*\$?" """.Trim());

        Assert.Empty(Lines().Where(line => literal.IsMatch(line.Text) && !line.Where.Contains("ServiceCollectionExtensions", StringComparison.Ordinal)).Select(line => line.Where));
    }

    /// <summary>The 400 only shows texts of the catalogue, so one that is not from there would reach nobody.</summary>
    [Theory]
    [InlineData("Application")]
    [InlineData("Web.Api")]
    public void Layer_OnlyRefusesRequestsWithATextOfTheCatalogue(string layer)
    {
        // Broken by our own code, never by a request.
        string[] invariants = ["VehicleChainingPlanner.cs", "SimulatedRepositories.cs"];

        Assert.Empty(Lines()
            .Where(line => line.Where.StartsWith(layer + "/", StringComparison.Ordinal)
                && Regex.IsMatch(line.Text, @"new (ArgumentException|KeyNotFoundException)\(")
                && !invariants.Any(line.Where.Contains))
            .Select(line => line.Where));
    }

    private static IEnumerable<(string Where, string Text)> Lines() =>
        from file in Directory.EnumerateFiles(Source, "*.cs", SearchOption.AllDirectories)
        let name = Path.GetRelativePath(Source, file).Replace('\\', '/')
        where !name.Contains("/obj/", StringComparison.Ordinal) && !name.Contains("/bin/", StringComparison.Ordinal)
            && !name.Contains("/Migrations/", StringComparison.Ordinal) && !name.StartsWith("DemoData/", StringComparison.Ordinal)
            && !name.StartsWith("Core/Localization/", StringComparison.Ordinal) && !Allowed.Contains(name)
        from line in File.ReadLines(file).Select((text, i) => (Text: text.Trim(), Number: i + 1))
        where !line.Text.StartsWith("//", StringComparison.Ordinal)
        select ($"{name}:{line.Number}", line.Text);

    private static string FindSource()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "DispatchGuard.slnx")))
        {
            folder = folder.Parent;
        }

        return Path.Combine(folder!.FullName, "src");
    }
}
