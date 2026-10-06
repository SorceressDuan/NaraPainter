namespace NaraDreamPainter.App.ViewModels;

/// <summary>
/// Display names for the 24 modes, one per enum value in enum order. The blend picker binds its
/// SelectedIndex straight to the mode, so the list order has to stay the enum order. The text itself
/// comes from the resource files, which is why this is a pass-through rather than a name generator.
/// </summary>
public static class BlendModeCatalog
{
    public static IReadOnlyList<string> Names => Strings.BlendModeNames;

    public static int Count => Strings.BlendModeNames.Count;
}
