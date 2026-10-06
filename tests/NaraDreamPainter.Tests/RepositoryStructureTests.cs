using System.Text.RegularExpressions;
using Xunit;

namespace NaraDreamPainter.Tests;

public class RepositoryStructureTests
{
    private static readonly string[] ScannedExtensions =
        [".cs", ".csproj", ".xaml", ".props", ".targets", ".ps1", ".sln", ".config", ".xml", ".manifest", ".json"];

    // The tokens are assembled from pieces so that this scanner's own source does not match the
    // patterns it looks for.
    private static readonly string[] AppleReferences =
    [
        "App" + "Kit",
        "UI" + "Kit",
        "Core" + "Graphics",
        "Core" + "Image",
        "NS" + "Object",
        "import" + " Swift",
        "Me" + "tal"
    ];

    private static readonly string[] BannedNamespaces =
    [
        "System" + ".Drawing",
        "System" + ".Windows.Forms",
        "Windows" + ".UI.Xaml"
    ];

    [Fact]
    public void CodeAndProjectFilesDoNotReferenceAppleFrameworks()
    {
        string[] offenders = FindMatches(ApplePattern());
        Assert.True(offenders.Length == 0,
            "Apple framework references found:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void CodeAndProjectFilesAvoidTheBannedMicrosoftNamespaces()
    {
        string[] offenders = FindMatches(new Regex(string.Join("|", BannedNamespaces.Select(Regex.Escape))));
        Assert.True(offenders.Length == 0,
            "Banned namespaces found:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheLicenseFileCarriesTheUpstreamText()
    {
        string root = RepositoryRoot();
        string license = File.ReadAllText(Path.Combine(root, "LICENSE"));
        string upstream = File.ReadAllText(Path.Combine(root, "legacy", "LICENSE.upstream"));

        Assert.Equal(Normalize(upstream), Normalize(license));
    }

    [Fact]
    public void TheReadmeCallsThisAnUnofficialPortWithNoAffiliation()
    {
        string readme = File.ReadAllText(Path.Combine(RepositoryRoot(), "README.md"));

        Assert.Contains("非官方", readme, StringComparison.Ordinal);
        Assert.Contains("无隶属", readme, StringComparison.Ordinal);
    }

    private static Regex ApplePattern() => new(
        "\\b(?:" + string.Join("|", AppleReferences.Select(Regex.Escape)) + ")\\b"
        + "|(?<!Windows\\.)(?<!Microsoft\\.)\\b" + "Foun" + "dation" + "\\b",
        RegexOptions.Compiled);

    private static string[] FindMatches(Regex pattern)
    {
        string root = RepositoryRoot();
        var found = new List<string>();
        foreach (string file in SourceFiles(root))
        {
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (pattern.IsMatch(lines[i]))
                {
                    found.Add($"{Path.GetRelativePath(root, file)}:{i + 1}: {lines[i].Trim()}");
                }
            }
        }
        return [.. found];
    }

    // The docs name the frameworks they replace, so the scan covers code and project files only.
    // Packaging output is excluded as well: the self-contained payload's deps.json lists real
    // Windows App SDK and BCL assemblies, whose names trip both scans and are none of our business.
    private static IEnumerable<string> SourceFiles(string root)
    {
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "legacy", ".tools", ".git", ".vs", "bin", "obj", "dist"
        };
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                if (ScannedExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) yield return file;
            }
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if (!skipped.Contains(Path.GetFileName(child))) pending.Push(child);
            }
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NaraDreamPainter.sln"))) return directory.FullName;
            directory = directory.Parent;
        }
        throw new InvalidOperationException($"No NaraDreamPainter.sln found above {AppContext.BaseDirectory}.");
    }

    private static string Normalize(string text) => text.Replace("\r\n", "\n").TrimEnd();
}
