namespace Core.Tests.Docs;

public class RegulatoryMatrixTests
{
    private const string Header = "| Article | Rule | Requirement | Status | SPEC | Tests |\n|---|---|---|---|---|---|\n";

    private const string TaggedClass = $$"""
        [Trait("{{RegulatoryMatrix.TraitName}}", "Art. 7")]
        public class BreakTests
        {
            [Fact]
            public void Driving_AfterBreak_Resets() { }

            [Fact]
            public void Driving_TooLong_Breaks() { }
        }
        """;

    [Fact]
    public void Problems_RealMatrixAndTests_None()
    {
        var root = RepositoryRoot();
        var matrix = File.ReadAllText(Path.Combine(root, "docs", "REGULATORY_MATRIX.md"));

        var problems = RegulatoryMatrix.Problems(matrix, TestSources(root));

        Assert.Empty(problems);
    }

    [Fact]
    public void Problems_ConsistentMatrix_None()
    {
        var matrix = Header + "| Art. 7 | `MaxContinuousDriving` (4.5h) + `MandatoryContinuousBreak` (45min) | r | s | 1 | `Driving_*` (2), `BreakTests` |\n";

        Assert.Empty(RegulatoryMatrix.Problems(matrix, Sources(TaggedClass)));
    }

    [Fact]
    public void Problems_CitedTestRenamed_Reported()
    {
        var matrix = Header + "| Art. 7 | rule | r | s | 1 | `Driving_AfterBreak_Resets`, `Driving_AfterBreak_IsReset` |\n";

        var problem = Assert.Single(RegulatoryMatrix.Problems(matrix, Sources(TaggedClass)));

        Assert.Contains("Driving_AfterBreak_IsReset", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Problems_TaggedClassCitedUnderAnotherArticle_Reported()
    {
        var matrix = Header + "| Art. 8 | rule | r | s | 1 | `BreakTests` |\n";

        var problems = RegulatoryMatrix.Problems(matrix, Sources(TaggedClass));

        Assert.Contains(problems, problem => problem.Contains("not tagged with that article", StringComparison.Ordinal));
        Assert.Contains(problems, problem => problem.Contains("no row of that article cites it", StringComparison.Ordinal));
    }

    [Fact]
    public void Problems_EngineTestWithoutTrait_Reported()
    {
        var untagged = new Dictionary<string, string>
        {
            ["Tests/Core.Tests/ValueObjects/NewRuleTests.cs"] =
                "public class NewRuleTests { [Fact] public void X() { LegalComplianceEvaluation.Validate(h, s, d); } }",
        };

        var problem = Assert.Single(RegulatoryMatrix.Problems(Header, untagged));

        Assert.Contains("NewRuleTests", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Problems_FigureNotTheConstant_Reported()
    {
        var matrix = Header + "| Art. 7 | `MaxContinuousDriving` (4h) | r | s | 1 | `BreakTests` |\n";

        var problem = Assert.Single(RegulatoryMatrix.Problems(matrix, Sources(TaggedClass)));

        Assert.Contains("4.5h", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Problems_CountNotTheNumberOfTests_Reported()
    {
        var matrix = Header + "| Art. 7 | rule | r | s | 1 | `BreakTests` (3) |\n";

        var problem = Assert.Single(RegulatoryMatrix.Problems(matrix, Sources(TaggedClass)));

        Assert.Contains("there are 2 tests", problem, StringComparison.Ordinal);
    }

    [Fact]
    public void Problems_RowWithoutTestsColumn_ReportedNotThrown()
    {
        var matrix = Header + "| Art. 7 | rule | r | s |\n";

        var problems = RegulatoryMatrix.Problems(matrix, Sources(TaggedClass));

        Assert.Contains(problems, problem => problem.Contains("4 cells instead of 6", StringComparison.Ordinal));
    }

    private static Dictionary<string, string> Sources(string source) => new() { ["Tests/Core.Tests/Other/BreakTests.cs"] = source };

    private static Dictionary<string, string> TestSources(string root) =>
        Directory.EnumerateFiles(Path.Combine(root, "Tests"), "*.cs", SearchOption.AllDirectories)
            .Select(file => (Relative: Path.GetRelativePath(root, file).Replace('\\', '/'), File: file))
            .Where(file => !file.Relative.Contains("/bin/", StringComparison.Ordinal) && !file.Relative.Contains("/obj/", StringComparison.Ordinal))
            .ToDictionary(file => file.Relative, file => File.ReadAllText(file.File));

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "DispatchGuard.slnx")))
        {
            folder = folder.Parent;
        }

        return folder!.FullName;
    }
}
