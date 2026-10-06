using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace NaraDreamPainter.App.Services;

/// <summary>Pulls the first file path out of a drop payload.</summary>
public static class DroppedFile
{
    public static async Task<string?> FirstPathAsync(DataPackageView data)
    {
        if (!data.Contains(StandardDataFormats.StorageItems)) return null;

        IReadOnlyList<IStorageItem> items = await data.GetStorageItemsAsync();
        foreach (IStorageItem item in items)
        {
            if (item is StorageFile file && !string.IsNullOrEmpty(file.Path)) return file.Path;
        }

        return null;
    }
}
