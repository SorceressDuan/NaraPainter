namespace NaraPainter.App;

/// <summary>
/// Display strings for one of the pickers, kept in resource order.
/// </summary>
/// <remarks>
/// A fresh list every read, paired with a property-changed notification on a language switch. That is
/// what makes a bound ComboBox re-read its whole <c>ItemsSource</c> and redraw the text of its current
/// selection: editing the items of the list it already holds updates the strings but often leaves a
/// closed ComboBox showing the old text.
/// </remarks>
public static class LocalizedLists
{
    public static IReadOnlyList<string> ShapeNames => [Strings.ShapeRectangle, Strings.ShapeEllipse];

    public static IReadOnlyList<string> ChannelNames =>
        [Strings.ChannelRgb, Strings.ChannelRed, Strings.ChannelGreen, Strings.ChannelBlue];

    public static IReadOnlyList<string> RangeNames =>
    [
        Strings.RangeMaster, Strings.RangeReds, Strings.RangeYellows, Strings.RangeGreens,
        Strings.RangeCyans, Strings.RangeBlues, Strings.RangeMagentas
    ];
}
