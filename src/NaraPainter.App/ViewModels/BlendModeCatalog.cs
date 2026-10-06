namespace NaraPainter.App.ViewModels;

/// <summary>
/// How many blend modes there are. The list order is the enum order, which is what lets the picker
/// bind its selection straight to the mode; the names themselves come from the resource files, so
/// each layer view model builds its own list rather than sharing one.
/// </summary>
public static class BlendModeCatalog
{
    // Read from the resources rather than from a list: this guards the index setters, and it has to be
    // right before any list has been built.
    public static int Count => Strings.BlendModeNames.Count;
}
