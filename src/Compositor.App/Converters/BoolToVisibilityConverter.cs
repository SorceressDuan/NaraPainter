using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Data;

namespace Compositor.App.Converters;

/// <summary>
/// Maps a bool to Visibility. Pass "Invert" as the converter parameter to hide when the value is true.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        bool flag = value is bool flagValue && flagValue;
        return (IsInverted(parameter) ? !flag : flag) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        bool flag = value is Visibility.Visible;
        return IsInverted(parameter) ? !flag : flag;
    }

    private static bool IsInverted(object parameter) =>
        parameter is string text && text.Equals("Invert", StringComparison.OrdinalIgnoreCase);
}
