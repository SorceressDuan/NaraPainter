using Windows.Foundation;

namespace Compositor.Compositing.Rendering;

/// <summary>
/// Where the document sits in the control: screen = pan + document * zoom, both in DIPs. Kept apart
/// from the control so the renderer can be exercised without a XAML tree.
/// </summary>
public readonly struct ViewTransform
{
    public ViewTransform(double zoom, double panX, double panY, double width, double height)
    {
        Zoom = zoom > 0 && double.IsFinite(zoom) ? zoom : 1;
        PanX = panX;
        PanY = panY;
        Width = width;
        Height = height;
    }

    public double Zoom { get; }

    public double PanX { get; }

    public double PanY { get; }

    public double Width { get; }

    public double Height { get; }

    public Point ToScreen(double documentX, double documentY) => new(PanX + (documentX * Zoom), PanY + (documentY * Zoom));

    public Point ToDocument(double screenX, double screenY) => new((screenX - PanX) / Zoom, (screenY - PanY) / Zoom);

    /// <summary>
    /// The part of the document that lands inside the control, rounded outwards so a partly visible
    /// edge pixel is still rendered. Empty when the document is scrolled off screen.
    /// </summary>
    public Rect VisibleDocumentRect(int documentWidth, int documentHeight)
    {
        double left = Math.Max(0, Math.Floor(-PanX / Zoom));
        double top = Math.Max(0, Math.Floor(-PanY / Zoom));
        double right = Math.Min(documentWidth, Math.Ceiling((Width - PanX) / Zoom));
        double bottom = Math.Min(documentHeight, Math.Ceiling((Height - PanY) / Zoom));
        if (right <= left || bottom <= top) return default;
        return new Rect(left, top, right - left, bottom - top);
    }
}
