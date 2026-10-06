using Compositor.Models.Services;
using Microsoft.UI.Xaml;
using Windows.Storage.Pickers;
using WinRT.Interop;

namespace Compositor.App.Services;

/// <summary>
/// Pickers for an unpackaged app. Each picker has to be associated with the window handle before it
/// is shown; without that the call fails with an access denied error instead of opening a dialog.
/// </summary>
public sealed class FileDialogService
{
    private readonly IntPtr _windowHandle;
    private readonly IImageCodec _codec;

    public FileDialogService(Window window, IImageCodec codec)
    {
        _windowHandle = WindowNative.GetWindowHandle(window);
        _codec = codec;
    }

    public async Task<string?> PickImageAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            ViewMode = PickerViewMode.Thumbnail
        };

        InitializeWithWindow.Initialize(picker, _windowHandle);
        foreach (string extension in _codec.SupportedReadExtensions) picker.FileTypeFilter.Add("." + extension);

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    public async Task<string?> PickExportPathAsync(string suggestedName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.PicturesLibrary,
            SuggestedFileName = suggestedName
        };

        InitializeWithWindow.Initialize(picker, _windowHandle);
        foreach ((string label, string[] extensions) in ExportChoices)
        {
            var filter = new List<string>();
            foreach (string extension in extensions)
            {
                if (_codec.SupportedWriteExtensions.Contains(extension)) filter.Add("." + extension);
            }

            if (filter.Count > 0) picker.FileTypeChoices.Add(label, filter);
        }

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    /// <summary>One picker entry per format; jpg/jpeg and tif/tiff are the same encoder.</summary>
    private static readonly (string Label, string[] Extensions)[] ExportChoices =
    [
        ("PNG image", ["png"]),
        ("JPEG image", ["jpg", "jpeg"]),
        ("WebP image", ["webp"]),
        ("BMP image", ["bmp"]),
        ("TIFF image", ["tif", "tiff"])
    ];
}
