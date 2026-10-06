using System.Text.RegularExpressions;
using System.Xml;
using Xunit;

namespace NaraDreamPainter.Tests;

public class LocalizationTests
{
    // CJK ideographs, their compatibility forms, CJK punctuation and fullwidth forms. The resource
    // files are the one place Chinese belongs, so they are not part of the scan.
    private static readonly Regex ChineseCharacters = new(
        "[\u3000-\u303f\u3400-\u4dbf\u4e00-\u9fff\uf900-\ufaff\uff00-\uffef]",
        RegexOptions.Compiled);

    [Fact]
    public void BothResourceFilesDefineTheSameKeys()
    {
        HashSet<string> neutral = ResourceKeys("Strings.resx");
        HashSet<string> chinese = ResourceKeys("Strings.zh-CN.resx");

        string[] missingInChinese = [.. neutral.Except(chinese).Order()];
        string[] missingInNeutral = [.. chinese.Except(neutral).Order()];

        Assert.True(missingInChinese.Length == 0, "Strings.zh-CN.resx is missing: " + string.Join(", ", missingInChinese));
        Assert.True(missingInNeutral.Length == 0, "Strings.resx is missing: " + string.Join(", ", missingInNeutral));
    }

    [Fact]
    public void AppSourcesUseNoChineseText()
    {
        string[] offenders = [.. ChineseLines()];
        Assert.True(offenders.Length == 0,
            "Chinese text found in app sources:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void StringsPropertiesAreBackedByResourceEntries()
    {
        HashSet<string> neutral = ResourceKeys("Strings.resx");
        HashSet<string> chinese = ResourceKeys("Strings.zh-CN.resx");
        string source = TestFiles.ReadText(Path.Combine(AppRoot(), "Strings.cs"));

        MatchCollection properties = Regex.Matches(source,
            @"public\s+static\s+string\s+(?<name>\w+)\s*=>\s*Localization\.Get\(""(?<key>[^""]+)""\)");
        Assert.True(properties.Count > 0, "Strings.cs declares no properties at all");

        foreach (Match property in properties)
        {
            string name = property.Groups["name"].Value;
            string key = property.Groups["key"].Value;

            Assert.True(neutral.Contains(key), $"{name} asks for '{key}', which Strings.resx does not define");
            Assert.True(chinese.Contains(key), $"{name} asks for '{key}', which Strings.zh-CN.resx does not define");
            Assert.Equal(name, key.Replace("_", string.Empty));
        }
    }

    [Fact]
    public void EveryKeyTheAppAsksForIsDefined()
    {
        HashSet<string> defined = ResourceKeys("Strings.resx");
        HashSet<string> translated = ResourceKeys("Strings.zh-CN.resx");
        var offenders = new List<string>();

        foreach (string file in AppFiles(".cs"))
        {
            string text = TestFiles.ReadText(file);
            foreach (Match match in Regex.Matches(text, @"Localization\.(?:Get|Format)\(""(?<key>[^""]+)"""))
            {
                string key = match.Groups["key"].Value;
                if (!defined.Contains(key)) offenders.Add($"{Relative(file)}: '{key}' is not in Strings.resx");
                if (!translated.Contains(key)) offenders.Add($"{Relative(file)}: '{key}' is not in Strings.zh-CN.resx");
            }
        }

        Assert.True(offenders.Count == 0, "unknown resource keys:" + Environment.NewLine + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheReadmeNamesTheProjectAndTheDisclaimer()
    {
        string readme = TestFiles.ReadText(Path.Combine(RepositoryRoot(), "README.md"));

        Assert.Contains("Nara Dream Painter", readme, StringComparison.Ordinal);
        Assert.Contains("NaraDreamPainter.exe", readme, StringComparison.Ordinal);
        Assert.Contains("非官方", readme, StringComparison.Ordinal);
        Assert.Contains("无隶属", readme, StringComparison.Ordinal);
    }

    private static HashSet<string> ResourceKeys(string fileName)
    {
        var document = new XmlDocument();
        document.LoadXml(TestFiles.ReadText(Path.Combine(AppRoot(), "Resources", fileName)));

        return [.. document.SelectNodes("/root/data")!.Cast<XmlNode>().Select(node => node.Attributes!["name"]!.Value)];
    }

    private static IEnumerable<string> ChineseLines()
    {
        foreach (string file in AppFiles(".cs").Concat(AppFiles(".xaml")))
        {
            string[] lines = TestFiles.ReadLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                if (ChineseCharacters.IsMatch(lines[i]))
                {
                    yield return $"{Relative(file)}:{i + 1}: {lines[i].Trim()}";
                }
            }
        }
    }

    private static IEnumerable<string> AppFiles(string extension)
    {
        var skipped = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj" };
        var pending = new Stack<string>();
        pending.Push(AppRoot());

        while (pending.Count > 0)
        {
            string directory = pending.Pop();
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                if (Path.GetExtension(file).Equals(extension, StringComparison.OrdinalIgnoreCase)) yield return file;
            }
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                if (!skipped.Contains(Path.GetFileName(child))) pending.Push(child);
            }
        }
    }

    private static string AppRoot() => Path.Combine(RepositoryRoot(), "src", "NaraDreamPainter.App");

    private static string Relative(string path) => Path.GetRelativePath(RepositoryRoot(), path);

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
}
