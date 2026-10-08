using System.Text.RegularExpressions;
using NaraPainter.App.Services;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// The guide's structure, and whether the words and pictures it points at are there.
/// </summary>
/// <remarks>
/// The steps live in JSON so the wording and the pictures can be changed without touching code, which
/// only works while the keys and the file names stay honest. These tests are what keeps them honest:
/// a step whose wording key is missing would show blank, and one pointing at a picture that cannot be
/// placed would show a broken frame.
/// </remarks>
public class TutorialTests
{
    /// <summary>Every picture is drawn at this size, which is what the designer works to.</summary>
    private const int ImageWidth = 800;
    private const int ImageHeight = 600;

    [Fact]
    public void TheGuideParses()
    {
        TutorialCatalog? catalog = Catalog();

        Assert.NotNull(catalog);
        Assert.Equal(1, catalog.Version);
        Assert.NotEmpty(catalog.Chapters);
        Assert.All(catalog.Chapters, chapter => Assert.NotEmpty(chapter.Steps));
    }

    [Fact]
    public void TheGuideCoversTheFourChapters()
    {
        string[] ids = [.. Catalog().Chapters.Select(chapter => chapter.Id)];

        Assert.Equal(["open", "layers", "heal", "export"], ids);
        Assert.Equal(4, Catalog().Chapters.Count);
        Assert.Equal(16, Catalog().Steps.Count());
    }

    [Fact]
    public void EveryStepKeyReadsInBothLanguages()
    {
        // A key that is only in one file leaves the guide half-translated, which the resource gate
        // would catch later; catching it here says which step is at fault.
        HashSet<string> english = ResourceKeys("Strings.resx");
        HashSet<string> chinese = ResourceKeys("Strings.zh-CN.resx");

        string[] offenders = [.. Keys()
            .Where(key => !english.Contains(key) || !chinese.Contains(key))
            .Select(key => $"{key}: en={english.Contains(key)} zh={chinese.Contains(key)}")];

        Assert.True(offenders.Length == 0,
            "A guide key is missing from a resource file:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TwoStepsDoNotShareOneKey()
    {
        string[] duplicates = [.. Keys().GroupBy(key => key).Where(group => group.Count() > 1).Select(group => group.Key)];

        Assert.True(duplicates.Length == 0,
            "Two steps point at the same wording:" + Environment.NewLine + string.Join(Environment.NewLine, duplicates));
    }

    [Fact]
    public void EveryPictureIsNamedForTheGuideFolder()
    {
        // The files ship with the guide, so a name that cannot be placed is a broken step.
        TutorialCatalog catalog = Catalog();

        Assert.False(string.IsNullOrWhiteSpace(catalog.ImageDirectory));

        string[] offenders = [.. catalog.Steps
            .Where(step => !IsUsableName(step.Image))
            .Select(step => $"{step.TextKey} names '{step.Image}'")];

        Assert.True(offenders.Length == 0,
            "A step names a picture that cannot be placed:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void EveryPictureNamedByTheGuideIsThere()
    {
        TutorialCatalog catalog = Catalog();
        string folder = PictureFolder(catalog);

        string[] missing = [.. catalog.Steps
            .Select(step => Path.Combine(folder, step.Image))
            .Where(path => !File.Exists(path))
            .Select(path => Path.GetFileName(path))];

        Assert.True(missing.Length == 0,
            "The guide names pictures that are not in the folder:" + Environment.NewLine
            + string.Join(Environment.NewLine, missing));
    }

    [Fact]
    public void EveryPictureIsTheSizeTheGuidePromises()
    {
        // The dialog lays the picture out at the size the JSON gives, so a file of another size would
        // be stretched. Read straight from the PNG header rather than decoding the whole image.
        TutorialCatalog catalog = Catalog();
        string folder = PictureFolder(catalog);

        string[] offenders = [.. catalog.Steps
            .Select(step => (step, path: Path.Combine(folder, step.Image)))
            .Where(pair => File.Exists(pair.path))
            .Select(pair => (pair.step, size: ReadPngSize(pair.path)))
            .Where(pair => pair.size != (pair.step.ImageWidth, pair.step.ImageHeight))
            .Select(pair => $"{pair.step.Image} is {pair.size.Item1}x{pair.size.Item2}, "
                + $"the guide says {pair.step.ImageWidth}x{pair.step.ImageHeight}")];

        Assert.True(offenders.Length == 0,
            "A picture is not the size the guide lays it out at:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>Width and height from a PNG's IHDR chunk, which sits at a fixed offset.</summary>
    private static (int Width, int Height) ReadPngSize(string path)
    {
        byte[] header = new byte[24];
        using FileStream stream = File.OpenRead(path);
        Assert.Equal(24, stream.Read(header, 0, header.Length));

        // 8 bytes of signature, 4 of chunk length, 4 of chunk name, then the two dimensions.
        Assert.Equal("PNG", System.Text.Encoding.ASCII.GetString(header, 1, 3));

        int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(16, 4));
        int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(20, 4));
        return (width, height);
    }

    private static string PictureFolder(TutorialCatalog catalog) =>
        Path.Combine(RepositoryRoot(), "src", "NaraPainter.App", catalog.ImageDirectory);

    [Fact]
    public void EveryPictureSitsWhereTheGuideLooks()
    {
        TutorialCatalog catalog = Catalog();
        string folder = Path.Combine(RepositoryRoot(), "src", "NaraPainter.App", catalog.ImageDirectory);

        string[] offenders = [.. catalog.Steps
            .Select(step => Path.Combine(folder, step.Image))
            .Where(path => !path.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            .Select(path => path)];

        Assert.True(offenders.Length == 0,
            "A picture resolves outside the guide's folder:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void EveryStepAsksForTheSamePictureSize()
    {
        string[] offenders = [.. Catalog().Steps
            .Where(step => step.ImageWidth < 1 || step.ImageHeight < 1)
            .Select(step => $"{step.TextKey} asks for {step.ImageWidth}x{step.ImageHeight}")];

        Assert.True(offenders.Length == 0,
            "A step asks for a picture with no size:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));

        Assert.All(Catalog().Steps, step =>
        {
            Assert.Equal(ImageWidth, step.ImageWidth);
            Assert.Equal(ImageHeight, step.ImageHeight);
        });
    }

    [Fact]
    public void TheGuideThatShipsWithTheAppCanBeRead()
    {
        // Load() reads beside the executable, which is where the build puts the asset. A failure here
        // means the JSON stopped being copied out, and the dialog would apologise at runtime instead.
        TutorialCatalog? catalog = TutorialCatalog.Load();

        Assert.NotNull(catalog);
        Assert.Equal(16, catalog.Steps.Count());
    }

    [Fact]
    public void AMalformedGuideIsRefusedRatherThanThrown()
    {
        Assert.Null(TutorialCatalog.Parse("{ this is not json"));
    }

    private static bool IsUsableName(string name) =>
        !string.IsNullOrWhiteSpace(name)
        && name.Equals(Path.GetFileName(name), StringComparison.Ordinal)
        && name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        && !name.Contains("..", StringComparison.Ordinal);

    private static TutorialCatalog Catalog()
    {
        string path = Path.Combine(RepositoryRoot(), "src", "NaraPainter.App", "Assets", "Tutorial", "tutorial.json");
        TutorialCatalog? catalog = TutorialCatalog.Parse(TestFiles.ReadText(path));

        Assert.NotNull(catalog);
        return catalog;
    }

    private static IEnumerable<string> Keys() =>
        Catalog().Chapters.SelectMany(chapter => chapter.Steps.Select(step => step.TextKey).Prepend(chapter.TitleKey));

    private static HashSet<string> ResourceKeys(string fileName)
    {
        string path = Path.Combine(RepositoryRoot(), "src", "NaraPainter.App", "Resources", fileName);
        var keys = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match match in Regex.Matches(TestFiles.ReadText(path), "<data name=\"([^\"]+)\""))
        {
            keys.Add(match.Groups[1].Value);
        }

        return keys;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NaraPainter.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException($"No NaraPainter.sln found above {AppContext.BaseDirectory}.");
    }
}
