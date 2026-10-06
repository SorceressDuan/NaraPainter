using System.Globalization;
using NaraDreamPainter.App;
using NaraDreamPainter.App.Services;
using Xunit;

namespace NaraDreamPainter.Tests;

/// <summary>
/// The language picker: which culture a start-up lands on, how the choice is remembered, and that the
/// resources behind it can actually label the picker.
/// </summary>
public class LanguageSwitchTests
{
    [Fact]
    public void ASavedSupportedLanguageWinsOverTheSystem()
    {
        Assert.Equal("zh-CN", LanguageCatalog.StartupCulture(new CultureInfo("zh-CN")).Name);
        Assert.Equal("en", LanguageCatalog.StartupCulture(new CultureInfo("en")).Name);
    }

    [Fact]
    public void AnUnsupportedSavedLanguageFallsBackInsteadOfBeingUsed()
    {
        CultureInfo culture = LanguageCatalog.StartupCulture(new CultureInfo("fr-FR"));

        // A stale or hand-edited settings file must not pin the app to a culture with no resources,
        // which would show every label as its key.
        Assert.NotEqual("fr-FR", culture.Name);
        Assert.Contains(LanguageCatalog.Options, option => option.Name == culture.Name);
    }

    [Fact]
    public void WithoutASavedLanguageTheSystemDecides()
    {
        CultureInfo original = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");
            Assert.Equal("zh-CN", LanguageCatalog.StartupCulture(null).Name);

            CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
            Assert.Equal("en", LanguageCatalog.StartupCulture(null).Name);
        }
        finally
        {
            CultureInfo.CurrentUICulture = original;
        }
    }

    [Fact]
    public void ThePickerFallsBackToTheLastOptionForAnUnknownCulture()
    {
        Assert.Equal(0, LanguageCatalog.IndexOf(new CultureInfo("zh-CN")));
        Assert.Equal(1, LanguageCatalog.IndexOf(new CultureInfo("en")));
        Assert.Equal(LanguageCatalog.Options.Count - 1, LanguageCatalog.IndexOf(new CultureInfo("fr-FR")));
    }

    [Fact]
    public void ASavedPreferenceComesBack()
    {
        string path = Scratch("settings.json");
        try
        {
            var preference = new LanguagePreference(path);

            preference.Save(new CultureInfo("zh-CN"));
            Assert.Equal("zh-CN", preference.Load()!.Name);

            // Saving again replaces the previous choice rather than appending to it.
            preference.Save(new CultureInfo("en"));
            Assert.Equal("en", preference.Load()!.Name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AMissingSettingsFileMeansNoPreference()
    {
        var preference = new LanguagePreference(Path.Combine(ScratchDirectory(), "nothing-here", "settings.json"));

        Assert.Null(preference.Load());
    }

    [Theory]
    [InlineData("{ not json")]
    [InlineData("{}")]
    [InlineData("{\"Language\":\"fr-FR\"}")]
    [InlineData("{\"Language\":\"not a culture\"}")]
    public void UnusableSettingsReadAsNoPreference(string contents)
    {
        string path = Scratch("unusable.json");
        try
        {
            File.WriteAllText(path, contents);

            Assert.Null(new LanguagePreference(path).Load());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void AnUnwritablePreferenceIsSwallowedInsteadOfThrowing()
    {
        string blocker = Scratch("blocker");
        File.WriteAllText(blocker, "not a directory");

        try
        {
            // A settings file that cannot be written must not stop the language from changing.
            var preference = new LanguagePreference(Path.Combine(blocker, "settings.json"));
            preference.Save(new CultureInfo("en"));

            Assert.Null(preference.Load());
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public void SwitchingCultureSwapsTheStringsBothWays()
    {
        Dictionary<string, string> neutral = LocalizationTests.ResourceValues("Strings.resx");
        Dictionary<string, string> chinese = LocalizationTests.ResourceValues("Strings.zh-CN.resx");
        CultureInfo original = Localization.Culture;
        int notifications = 0;
        EventHandler handler = (_, _) => notifications++;

        try
        {
            Localization.CultureChanged += handler;

            Localization.Culture = new CultureInfo("en");
            Assert.Equal(neutral["Toolbar_Open"], Strings.ToolbarOpen);

            Localization.Culture = new CultureInfo("zh-CN");
            Assert.Equal(chinese["Toolbar_Open"], Strings.ToolbarOpen);

            int before = notifications;
            Localization.Culture = new CultureInfo("zh-CN");
            Assert.Equal(before, notifications);

            Assert.True(notifications >= 1, "switching languages never raised CultureChanged");
        }
        finally
        {
            Localization.CultureChanged -= handler;
            Localization.Culture = original;
        }
    }

    private static string Scratch(string name)
    {
        Directory.CreateDirectory(ScratchDirectory());
        return Path.Combine(ScratchDirectory(), name);
    }

    private static string ScratchDirectory() => Path.Combine(AppContext.BaseDirectory, "language-scratch");
}
