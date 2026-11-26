using System.Text.RegularExpressions;

namespace Core.Tests.Docs;

/// <summary>A mitigation is only as good as the test or file it cites: a rename must not leave the threat model pointing nowhere.</summary>
public partial class ThreatModelTests
{
    [Fact]
    public void Citations_TestsMethodsAndFiles_AllExist()
    {
        var root = RepositoryRoot();
        var model = File.ReadAllText(Path.Combine(root, "docs", "THREAT_MODEL.md"));
        var tests = string.Join('\n', Directory.EnumerateFiles(Path.Combine(root, "Tests"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(File.ReadAllText));

        var missing = Cited().Matches(model).Select(match => match.Groups["name"].Value)
            .Where(name => !Exists(name, root, tests))
            .ToList();

        Assert.Empty(missing);
    }

    private static bool Exists(string name, string root, string tests) => name switch
    {
        _ when name.Contains(':') => true, // a configuration key, not a file
        _ when name.Contains('/') && Path.HasExtension(name) && !name.StartsWith('/') => File.Exists(Path.Combine(root, name)),
        _ when name.EndsWith("Tests", StringComparison.Ordinal) => Regex.IsMatch(tests, $@"class\s+{name}\b"),
        _ when name.Contains('_') => Regex.IsMatch(tests, $@"(?:void|Task)\s+{name}\s*\("),
        _ => true,
    };

    [GeneratedRegex(@"`(?<name>[A-Za-z0-9_./-]+)`")]
    private static partial Regex Cited();

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
