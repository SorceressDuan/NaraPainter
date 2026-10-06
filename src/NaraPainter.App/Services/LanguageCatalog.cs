using System.Globalization;

namespace NaraPainter.App.Services;

/// <summary>One entry of the language picker. The label is read late so it follows the resources.</summary>
public sealed record LanguageOption(string Name, Func<string> ReadLabel)
{
    public string DisplayName => ReadLabel();
}

/// <summary>
/// The languages this build ships. Adding one means adding its <c>Strings.&lt;culture&gt;.resx</c> and a
/// line here; nothing else in the app knows the list.
/// </summary>
public static class LanguageCatalog
{
    public static IReadOnlyList<LanguageOption> Options { get; } =
    [
        new("zh-CN", () => Strings.LanguageChinese),
        new("en", () => Strings.LanguageEnglish)
    ];

    /// <summary>What an unrecognised system language falls back to.</summary>
    public static LanguageOption Default => Options[1];

    /// <summary>
    /// Where the picker should stand on start-up: the saved choice wins, then a Chinese system gets
    /// Chinese, and anything else gets English.
    /// </summary>
    public static CultureInfo StartupCulture(CultureInfo? saved)
    {
        if (saved is not null && Options.Any(option => option.Name == saved.Name)) return saved;

        string system = CultureInfo.CurrentUICulture.Name;
        return system.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? new CultureInfo("zh-CN")
            : new CultureInfo("en");
    }

    public static int IndexOf(CultureInfo culture)
    {
        for (int i = 0; i < Options.Count; i++)
        {
            if (Options[i].Name == culture.Name) return i;
        }
        return Options.Count - 1;
    }
}
