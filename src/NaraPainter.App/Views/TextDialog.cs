using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace NaraPainter.App.Views;

/// <summary>
/// Asks for one line of text. Deliberately the whole of the text tool's interface: font, size and
/// colour are fixed for now, so the dialog collects the string and nothing else.
/// </summary>
internal static class TextDialog
{
    /// <summary>The string as typed, or null when the dialog was cancelled or left blank.</summary>
    public static async Task<string?> ShowAsync(XamlRoot root)
    {
        var input = new TextBox
        {
            Header = Strings.TextPrompt,
            Text = Strings.TextPrompt,
            AcceptsReturn = false,
            SelectionStart = 0
        };

        var dialog = new ContentDialog
        {
            Title = Strings.TextDialogTitle,
            Content = input,
            PrimaryButtonText = Strings.DialogOk,
            CloseButtonText = Strings.DialogCancel,
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = root
        };

        // A dialog is outside the window tree, so the app font has to be put on it by hand or the
        // Chinese in it renders in whatever the shell default happens to be.
        if (Application.Current.Resources.TryGetValue("AppFontFamily", out object? family) && family is FontFamily appFont)
        {
            dialog.FontFamily = appFont;
        }

        input.SelectAll();
        input.Loaded += (_, _) => input.Focus(FocusState.Programmatic);

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;

        string typed = input.Text?.Trim() ?? string.Empty;
        return typed.Length == 0 ? null : typed;
    }
}
