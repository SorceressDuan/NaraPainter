using Microsoft.UI.Xaml.Data;

namespace NaraPainter.App.Converters;

/// <summary>
/// Maps "can this be used" onto how solid the control looks: full when it can, faded when it cannot.
/// </summary>
/// <remarks>
/// Used instead of <c>IsEnabled</c> on the buttons that depend on there being something to act on.
/// A disabled control receives no pointer messages, so its tooltip never opens and a new user is left
/// with a grey button and no explanation. Fading an enabled control keeps the hint readable; the
/// handlers behind these buttons already return without doing anything when the action is not
/// available, so a click on a faded one is harmless.
/// </remarks>
public sealed class EnabledToOpacityConverter : IValueConverter
{
    /// <summary>How solid a control looks when its action is not available.</summary>
    public const double Faded = 0.4;

    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is bool available && available ? 1.0 : Faded;

    public object ConvertBack(object value, Type targetType, object parameter, string language) =>
        throw new NotSupportedException();
}
