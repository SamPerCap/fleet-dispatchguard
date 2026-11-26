using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using DispatchGuard.Core.ValueObjects;

namespace Core.Tests.Docs;

/// <summary>
/// Reads <c>docs/REGULATORY_MATRIX.md</c> next to the test sources, so that the matrix a reviewer reads cannot drift from the tests
/// that back it or from <see cref="DrivingLimits"/>. Works on text, not on loaded assemblies: the integration tests live in another
/// project that this one does not reference.
/// </summary>
internal static partial class RegulatoryMatrix
{
    public const string TraitName = "Regulation";

    // They call the engine to read what it measured or to format it, and assert no rule of their own.
    private static readonly HashSet<string> NotRuleTests = ["ComplianceRuleCheckTests", "DisplayFormatTests", "DriverHoursTests"];

    private sealed record Row(string Article, string Rule, IReadOnlyList<Citation> Tests);

    private sealed record Citation(string Name, int? Count);

    private sealed record TestClass(string Name, string Path, IReadOnlyList<string> Articles, IReadOnlyList<string> Methods, int TestCount, string Source);

    public static IReadOnlyList<string> Problems(string matrix, IReadOnlyDictionary<string, string> testSources)
    {
        var lines = TableLines(matrix);
        var rows = lines
            .Where(cells => cells.Length >= 7)
            .Select(cells => new Row(cells[1].Trim(), cells[2].Trim(), Citations(cells[6])))
            .ToList();
        var classes = Classes(testSources);
        var problems = lines
            .Where(cells => cells.Length < 7)
            .Select(cells => $"A row of the matrix has {cells.Length - 2} cells instead of 6: {string.Join('|', cells)}")
            .ToList();

        foreach (var row in rows)
        {
            problems.AddRange(FigureProblems(row));
            problems.AddRange(row.Tests.SelectMany(citation => CitationProblems(row, citation, classes)));
        }

        foreach (var testClass in classes)
        {
            problems.AddRange(
                from article in testClass.Articles
                where !rows.Any(row => row.Article == article && row.Tests.Any(citation => OwnersOf(citation, classes).Contains(testClass)))
                select $"{testClass.Name} is tagged \"{article}\" but no row of that article cites it or one of its tests");

            if (testClass.Articles.Count == 0 && AssertsTheEngine(testClass))
            {
                problems.Add($"{testClass.Name} asserts the CE 561/2006 engine without [Trait(\"{TraitName}\", \"<Article>\")]");
            }
        }

        return problems;
    }

    private static IEnumerable<string> CitationProblems(Row row, Citation citation, IReadOnlyList<TestClass> classes)
    {
        var owners = OwnersOf(citation, classes);
        if (owners.Count == 0)
        {
            yield return $"`{citation.Name}` is cited in \"{row.Article}\" but no test class or test method has that name";
            yield break;
        }

        foreach (var owner in owners.Where(owner => !owner.Articles.Contains(row.Article)))
        {
            yield return $"`{citation.Name}` is cited in \"{row.Article}\" but {owner.Name} is not tagged with that article";
        }

        if (citation.Count is { } written && CountOf(citation, owners) != written)
        {
            yield return $"`{citation.Name}` ({written}) in \"{row.Article}\": there are {CountOf(citation, owners)} tests";
        }
    }

    private static List<TestClass> OwnersOf(Citation citation, IReadOnlyList<TestClass> classes)
    {
        if (citation.Name.EndsWith('*'))
        {
            var prefix = citation.Name.TrimEnd('*');
            return classes.Where(c => c.Methods.Any(method => method.StartsWith(prefix, StringComparison.Ordinal))).ToList();
        }

        return classes.Where(c => c.Name == citation.Name || c.Methods.Contains(citation.Name)).ToList();
    }

    private static int CountOf(Citation citation, IReadOnlyList<TestClass> owners)
    {
        if (!citation.Name.EndsWith('*'))
        {
            return owners.Where(owner => owner.Name == citation.Name).Sum(owner => owner.TestCount);
        }

        var prefix = citation.Name.TrimEnd('*');
        return owners.Sum(owner => owner.Methods.Count(method => method.StartsWith(prefix, StringComparison.Ordinal)));
    }

    private static bool AssertsTheEngine(TestClass testClass) =>
        testClass.Path.StartsWith("Tests/Core.Tests/ValueObjects/", StringComparison.Ordinal)
        && testClass.Source.Contains("LegalComplianceEvaluation.Validate(", StringComparison.Ordinal)
        && !NotRuleTests.Contains(testClass.Name);

    // The figure written right after a constant of DrivingLimits, e.g. `MaxContinuousDriving` (4.5h), must be its value.
    private static IEnumerable<string> FigureProblems(Row row)
    {
        foreach (Match match in ConstantWithFigure().Matches(row.Rule))
        {
            var field = typeof(DrivingLimits).GetField(match.Groups["name"].Value, BindingFlags.Public | BindingFlags.Static);
            if (field is null) continue;

            var written = match.Groups["figure"].Value;
            var actual = Figure(field.GetValue(null));
            if (written != actual)
            {
                yield return $"\"{row.Article}\": `{field.Name}` is written ({written}) but DrivingLimits says {actual}";
            }
        }
    }

    private static string Figure(object? value) => value switch
    {
        TimeSpan span when span.TotalHours >= 1 => span.TotalHours.ToString(CultureInfo.InvariantCulture) + "h",
        TimeSpan span => span.TotalMinutes.ToString(CultureInfo.InvariantCulture) + "min",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    private static List<string[]> TableLines(string matrix)
    {
        var lines = matrix.ReplaceLineEndings("\n").Split('\n');
        var header = Array.FindIndex(lines, line => line.StartsWith("| Article |", StringComparison.Ordinal));
        if (header < 0) return [];

        return lines
            .Skip(header + 2)
            .TakeWhile(line => line.StartsWith('|'))
            .Select(line => line.Split('|'))
            .ToList();
    }

    private static List<Citation> Citations(string cell) =>
        CitedName().Matches(cell)
            .Select(match => new Citation(
                match.Groups["name"].Value,
                match.Groups["count"].Success ? int.Parse(match.Groups["count"].Value, CultureInfo.InvariantCulture) : null))
            .ToList();

    private static List<TestClass> Classes(IReadOnlyDictionary<string, string> testSources) =>
        testSources
            .Select(source => (source.Key, source.Value, Declaration: ClassDeclaration().Match(source.Value)))
            .Where(file => file.Declaration.Success)
            .Select(file => new TestClass(
                file.Declaration.Groups["name"].Value,
                file.Key,
                RegulationTrait().Matches(file.Value).Select(match => match.Groups["article"].Value).ToList(),
                TestMethod().Matches(file.Value).Select(match => match.Groups["name"].Value).ToList(),
                TestAttribute().Count(file.Value),
                file.Value))
            .ToList();

    [GeneratedRegex(@"`(?<name>\w+)`\s*\((?<figure>[^\s,)]+)")]
    private static partial Regex ConstantWithFigure();

    [GeneratedRegex(@"`(?<name>[A-Za-z0-9_]+\*?)`(?:\s*\((?<count>\d+)\))?")]
    private static partial Regex CitedName();

    [GeneratedRegex(@"public\s+(?:sealed\s+|partial\s+)*class\s+(?<name>\w+)")]
    private static partial Regex ClassDeclaration();

    [GeneratedRegex(@"\[Trait\(""Regulation"",\s*""(?<article>[^""]+)""\)\]")]
    private static partial Regex RegulationTrait();

    [GeneratedRegex(@"public\s+(?:async\s+)?(?:void|Task)\s+(?<name>\w+)\s*\(")]
    private static partial Regex TestMethod();

    // Only attributes that start a line: a "[Fact]" inside a string is not a test.
    [GeneratedRegex(@"^\s*\[(?:Fact|Theory)\b", RegexOptions.Multiline)]
    private static partial Regex TestAttribute();
}
