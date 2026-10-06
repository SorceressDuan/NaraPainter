using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NaraPainter.App.ViewModels;

/// <summary>
/// Hand-rolled INotifyPropertyChanged. The offline feed has no MVVM toolkit, and the view models here
/// are small enough that one base class covers all of them.
/// </summary>
public abstract class ObservableObject : INotifyPropertyChanged
{
    /// <summary>
    /// The resource layer, so a data template can bind its own text without a named element:
    /// <c>{x:Bind Text.LayersVisibilityHint}</c> with <c>x:DataType</c> set to the item type.
    /// </summary>
    public LocalizedStrings Text => LocalizedStrings.Instance;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
