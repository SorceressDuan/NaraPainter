using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace NaraPainter.App.Views;

/// <summary>
/// The About box: what this is, which version, under what licence, and where it came from.
/// </summary>
internal static class AboutDialog
{
    /// <summary>Filled in when the artwork is handed over; the row is hidden while it is empty.</summary>
    private const string Designer = "";

    public static async Task ShowAsync(XamlRoot root)
    {
        var text = new StackPanel { Spacing = 6 };

        text.Children.Add(Line(Strings.AppTitle, strong: true));
        text.Children.Add(Line(AppVersion()));
        text.Children.Add(Line(Strings.AboutLicense));
        text.Children.Add(Line(Strings.AboutUpstream));

        var link = new HyperlinkButton
        {
            Content = Strings.AboutUpstreamLink,
            NavigateUri = new Uri("https://github.com/robbietilton/Compositor"),
            Padding = new Thickness(0)
        };
        text.Children.Add(link);

        text.Children.Add(Line(Strings.AboutUnofficial));

        if (Designer.Length > 0) text.Children.Add(Line(Strings.AboutDesignerLabel + Designer));

        var dialog = new ContentDialog
        {
            Title = Strings.AboutTitle,
            Content = text,
            CloseButtonText = Strings.DialogOk,
            XamlRoot = root
        };

        // A dialog is hosted outside the window's tree, so AppFontFamily does not reach it by
        // inheritance the way it does for the panels.
        if (Application.Current.Resources.TryGetValue("AppFontFamily", out object? family) && family is FontFamily font)
        {
            dialog.FontFamily = font;
        }

        await dialog.ShowAsync();
    }

    private static TextBlock Line(string value, bool strong = false) => new()
    {
        Text = value,
        TextWrapping = TextWrapping.Wrap,
        Style = (Style)Application.Current.Resources[strong ? "BodyStrongTextBlockStyle" : "BodyTextBlockStyle"]
    };

    private static string AppVersion()
    {
        Version? version = typeof(AboutDialog).Assembly.GetName().Version;
        return Localization.Interpolate(Strings.AboutVersion, version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}");
    }
}
