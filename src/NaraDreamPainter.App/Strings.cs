namespace NaraDreamPainter.App;

/// <summary>
/// Typed access to every UI string. Keys are grouped by the surface they appear on, which is also how
/// the .resx files are ordered, so a missing translation is easy to spot on both sides.
/// </summary>
/// <remarks>
/// These stay as expression-bodied properties rather than fields: a field would capture the culture
/// at type-initialisation time and keep showing the old language after a switch.
/// </remarks>
public static class Strings
{
    // Window and status bar.
    public static string AppTitle => Localization.Get("App_Title");

    public static string AppUntitled => Localization.Get("App_Untitled");

    public static string AppReady => Localization.Get("App_Ready");
}
