using System.Globalization;
using System.Resources;
using NaraDreamPainter.App.Services;

namespace NaraDreamPainter.App;

/// <summary>
/// Resolves UI text from Resources/Strings.resx and its satellite cultures.
/// </summary>
/// <remarks>
/// Chinese unless the system says otherwise; a saved choice from the language picker wins over both.
/// Changing <see cref="Culture"/> raises <see cref="CultureChanged"/>, which is what the bound labels
/// listen to - no window is rebuilt and no restart is needed. Adding a language is a
/// <c>Strings.&lt;culture&gt;.resx</c> plus an entry in <see cref="LanguageCatalog"/>.
/// </remarks>
public static class Localization
{
    private static readonly ResourceManager Resources = new(
        "NaraDreamPainter.App.Resources.Strings",
        typeof(Localization).Assembly);

    private static readonly LanguagePreference Preference = new();

    private static CultureInfo _culture = LanguageCatalog.StartupCulture(Preference.Load());

    static Localization() => CultureInfo.CurrentUICulture = _culture;

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
            Preference.Save(value);
            CultureChanged?.Invoke(null, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Looked up by name. Prefer the typed accessors on <see cref="Strings"/>; this exists for the few
    /// places that need a key built at runtime.
    /// </summary>
    public static string Get(string key)
    {
        string? value = Resources.GetString(key, _culture);
        return value ?? $"!{key}!";
    }

    /// <summary>
    /// Fills the placeholders of a template that has already been resolved, such as
    /// <c>Strings.StatusOpened</c>. Interface code reaches the templates through the typed accessors,
    /// so this is what it calls; there is deliberately no key-based variant, because handing a key to a
    /// formatter prints the key back instead of the text.
    /// </summary>
    public static string Interpolate(string template, params object?[] arguments) =>
        string.Format(_culture, template, arguments);
}
