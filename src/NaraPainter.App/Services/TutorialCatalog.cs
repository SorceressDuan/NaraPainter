using System.Text.Json;
using System.Text.Json.Serialization;

namespace NaraPainter.App.Services;

/// <summary>
/// The how-to steps, read from <c>Assets/Tutorial/tutorial.json</c>.
/// </summary>
/// <remarks>
/// Words live in the resource files and are named here by key, so a step reads in both languages
/// without the file being duplicated. Pictures are named rather than embedded, so the designer can
/// swap one by dropping a file in and never touching code — a step whose picture is not there yet
/// still shows, as a placeholder the size the picture is meant to be.
/// </remarks>
public sealed class TutorialCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    [JsonPropertyName("version")]
    public int Version { get; init; }

    /// <summary>Folder the picture names are relative to, below the application directory.</summary>
    [JsonPropertyName("imageDirectory")]
    public string ImageDirectory { get; init; } = string.Empty;

    [JsonPropertyName("chapters")]
    public IReadOnlyList<TutorialChapter> Chapters { get; init; } = [];

    /// <summary>Every step in reading order, which is the order the dialog walks through them.</summary>
    public IEnumerable<TutorialStep> Steps => Chapters.SelectMany(chapter => chapter.Steps);

    /// <summary>Reads the catalog that ships with the app, or null when it cannot be read.</summary>
    public static TutorialCatalog? Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, FileName);
        try
        {
            return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // A missing or malformed guide is not worth taking the app down for: the dialog says so.
            return null;
        }
    }

    /// <summary>
    /// Reads a catalog from text, or null when the text is not a catalog. Separated from the file so a
    /// test can hand it its own.
    /// </summary>
    public static TutorialCatalog? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<TutorialCatalog>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>Where a step's picture is, whether or not it has been drawn yet.</summary>
    public string PathOf(TutorialStep step) =>
        Path.Combine(AppContext.BaseDirectory, ImageDirectory, step.Image);

    /// <summary>The file name the guide is stored under, relative to the application directory.</summary>
    public const string FileName = "Assets/Tutorial/tutorial.json";
}

/// <summary>One section of the guide, listed down the side of the dialog.</summary>
public sealed class TutorialChapter
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = string.Empty;

    /// <summary>Resource key for the chapter name.</summary>
    [JsonPropertyName("titleKey")]
    public string TitleKey { get; init; } = string.Empty;

    [JsonPropertyName("steps")]
    public IReadOnlyList<TutorialStep> Steps { get; init; } = [];
}

/// <summary>One step: a line of text and the picture that goes with it.</summary>
public sealed class TutorialStep
{
    /// <summary>Resource key for the step's wording.</summary>
    [JsonPropertyName("textKey")]
    public string TextKey { get; init; } = string.Empty;

    [JsonPropertyName("image")]
    public string Image { get; init; } = string.Empty;

    /// <summary>How wide the picture is meant to be, which is what the placeholder is drawn at.</summary>
    [JsonPropertyName("imageWidth")]
    public int ImageWidth { get; init; }

    /// <summary>How tall the picture is meant to be.</summary>
    [JsonPropertyName("imageHeight")]
    public int ImageHeight { get; init; }
}
