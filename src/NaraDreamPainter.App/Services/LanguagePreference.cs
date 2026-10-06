using System.Globalization;
using System.Text.Json;

namespace NaraDreamPainter.App.Services;

/// <summary>
/// Remembers which language the user picked, so the next start comes up in it.
/// </summary>
/// <remarks>
/// Stored as JSON under <c>%LOCALAPPDATA%\NaraDreamPainter\settings.json</c>. Every failure here is
/// swallowed on purpose: a settings file that cannot be read or written must not stop the app from
/// starting or the language from changing for this session.
/// </remarks>
public sealed class LanguagePreference
{
    private const string FolderName = "NaraDreamPainter";
    private const string FileName = "settings.json";

    private readonly string _path;

    public LanguagePreference()
        : this(System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            FolderName,
            FileName))
    {
    }

    public LanguagePreference(string path) => _path = path;

    public string Path => _path;

    /// <summary>The saved language, or null when nothing usable is stored yet.</summary>
    public CultureInfo? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;

            string json = File.ReadAllText(_path);
            Settings? settings = JsonSerializer.Deserialize<Settings>(json);
            if (string.IsNullOrWhiteSpace(settings?.Language)) return null;

            var culture = new CultureInfo(settings.Language);
            return IsSupported(culture) ? culture : null;
        }
        catch (Exception error) when (error is IOException or JsonException or ArgumentException)
        {
            return null;
        }
    }

    public void Save(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);

        try
        {
            string? directory = System.IO.Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            string json = JsonSerializer.Serialize(new Settings { Language = culture.Name });
            File.WriteAllText(_path, json);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // Losing the preference is not worth failing a language change over, but silence here has
            // bitten before, so leave a trace next to the crash log.
            StartupLog.Record("language", $"could not save {_path}: {error.GetType().Name}");
        }
    }

    /// <summary>
    /// True for a language the build actually carries resources for. Without this a stale or
    /// hand-edited file could pin the app to a culture with no strings, and every label would come
    /// back as its key.
    /// </summary>
    private static bool IsSupported(CultureInfo culture) =>
        LanguageCatalog.Options.Any(option => option.Name == culture.Name);

    private sealed class Settings
    {
        public string? Language { get; set; }
    }
}
