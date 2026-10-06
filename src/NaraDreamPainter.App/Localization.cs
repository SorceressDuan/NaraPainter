using System.Globalization;
using System.Resources;

namespace NaraDreamPainter.App;

/// <summary>
/// Resolves UI text from Resources/Strings.resx and its satellite cultures.
/// </summary>
/// <remarks>
/// The app ships Simplified Chinese as its default language rather than following the system, because
/// that is who this build is for; an English system falls back to the neutral English resources
/// instead of showing Chinese. Adding a language means adding a Strings.&lt;culture&gt;.resx and
/// nothing else - the lookups below go through ResourceManager, so no code has to know the list.
/// </remarks>
public static class Localization
{
    private static readonly ResourceManager Resources = new(
        "NaraDreamPainter.App.Resources.Strings",
        typeof(Localization).Assembly);

    private static CultureInfo _culture = PickStartupCulture();

    /// <summary>Raised after the language changes, so open windows can re-read their text.</summary>
    public static event EventHandler? CultureChanged;

    public static CultureInfo Culture
    {
        get => _culture;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (_culture.Name == value.Name) return;
            _culture = value;
            CultureInfo.CurrentUICulture = value;
            CultureChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Chinese by default, English when the system is not Chinese. A system set to any other language
    /// gets English, which is the neutral resource file.
    /// </summary>
    private static CultureInfo PickStartupCulture()
    {
        string system = CultureInfo.CurrentUICulture.Name;
        bool chinese = system.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        var culture = new CultureInfo(chinese ? "zh-CN" : "en");
        CultureInfo.CurrentUICulture = culture;
        return culture;
    }

    /// <summary>
    /// Looked up by name. Prefer the generated properties on <see cref="Strings"/>; this exists for
    /// the few places that build a key at runtime, such as the language menu.
    /// </summary>
    public static string Get(string key)
    {
        string? value = Resources.GetString(key, _culture);
        return value ?? $"!{key}!";
    }

    public static string Format(string key, params object?[] arguments) =>
        string.Format(_culture, Get(key), arguments);
}
