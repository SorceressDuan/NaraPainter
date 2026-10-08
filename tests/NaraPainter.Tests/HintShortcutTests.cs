using System.Text.RegularExpressions;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// Keeps the shortcut written in a tooltip in step with the one the window actually binds.
/// </summary>
/// <remarks>
/// The two live apart: the gesture is declared on the window, the wording is a resource string, and a
/// tooltip that names a key nothing listens for is worse than one that names nothing at all. Export
/// moved from Ctrl+E to Ctrl+S and two hints went on saying Ctrl+E, which is what this is for.
/// </remarks>
public class HintShortcutTests
{
    /// <summary>
    /// A modifier sequence followed by a key, stopping before a closing bracket or the end of the text.
    /// The trailing <c>(?![A-Za-z+])</c> is what keeps <c>Ctrl+S</c> from matching inside
    /// <c>Ctrl+Shift+N</c>.
    /// </summary>
    private static readonly Regex ModifierChord = new(
        @"\b(?:Ctrl|Alt|Shift)(?:\+(?:Ctrl|Alt|Shift))*\+(?:\+\+|[-0-9A-Z])(?![A-Za-z+])");

    /// <summary>A bare tool key, which only counts on its own inside brackets.</summary>
    private static readonly Regex BracketedToolKey = new(@"\((?:M|I|J|Esc)\)");

    [Fact]
    public void EveryShortcutNamedInATooltipIsOneTheWindowBinds()
    {
        var bound = BoundGestures();
        Assert.NotEmpty(bound);

        string[] offenders = [.. Hints()
            .SelectMany(hint => Named(hint.Value).Select(gesture => (hint.Key, gesture)))
            .Where(pair => !bound.Contains(pair.gesture))
            .Select(pair => $"{pair.Key} names '{pair.gesture}', which nothing binds")];

        Assert.True(offenders.Length == 0,
            "A tooltip promised a key that does nothing:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// The hints whose key is worth pinning, because the key and the wording moved at different times.
    /// </summary>
    /// <remarks>
    /// The test above only proves a key is bound by something. It cannot tell that Export went on
    /// claiming Ctrl+E after Ctrl+E became merge, because merge really is bound to Ctrl+E and the name
    /// checks out. These are the pairs where that silent swap has actually happened, so they are pinned
    /// by name. Kept short on purpose: it is a record of past mistakes, not a second shortcut table.
    /// </remarks>
    private static readonly (string Key, string Gesture)[] PinnedHints =
    [
        ("Toolbar_ExportHint", "Ctrl+S"),
        ("Toolbar_OpenHint", "Ctrl+O"),
        ("Layers_MergeDownHint", "Ctrl+E"),
        ("Layers_MergeGroupHint", "Ctrl+Shift+E"),
        ("Layers_GroupHint", "Ctrl+G"),
        ("Layers_UngroupHint", "Ctrl+Shift+G"),
        ("Layers_AddHint", "Ctrl+N"),
        ("Toolbar_NewLayerHint", "Ctrl+N"),
        ("Layers_DuplicateHint", "Ctrl+J"),
        ("Toolbar_TextHint", "Ctrl+T")
    ];

    [Fact]
    public void TheHintsThatMovedStillNameTheRightKey()
    {
        var hints = Hints().ToDictionary(hint => hint.Key, hint => hint.Value, StringComparer.Ordinal);

        string[] offenders = [.. PinnedHints
            .Where(pin => !hints.TryGetValue(pin.Key, out string? value)
                || !Named(value).Contains(pin.Gesture, StringComparer.Ordinal))
            .Select(pin => $"{pin.Key} should name {pin.Gesture}")];

        Assert.True(offenders.Length == 0,
            "A tooltip no longer names the key it is pinned to:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>
    /// Gestures the window's table binds, read from the table's own declaration.
    /// </summary>
    /// <remarks>
    /// Split on the entry start rather than matched whole: a table entry spans two modifiers and a
    /// pipe, which a single pattern over the source is easy to get subtly wrong.
    /// </remarks>
    private static HashSet<string> BoundGestures()
    {
        string source = TestFiles.ReadText(Path.Combine(RepositoryRoot(), "src", "NaraPainter.App", "Views", "MainWindow.xaml.cs"));
        var bound = new HashSet<string>(StringComparer.Ordinal);

        foreach (string entry in source.Split("new(VirtualKey.").Skip(1))
        {
            int end = entry.IndexOf(')');
            if (end < 0) continue;

            Match gesture = Regex.Match(entry[..end], "\"([^\"]+)\"");
            if (gesture.Success) bound.Add(gesture.Groups[1].Value);
        }

        return bound;
    }

    private static IEnumerable<(string Key, string Value)> Hints()
    {
        string path = Path.Combine(RepositoryRoot(), "src", "NaraPainter.App", "Resources", "Strings.zh-CN.resx");
        string content = TestFiles.ReadText(path);

        foreach (Match match in Regex.Matches(content, "<data name=\"([^\"]+)\"[^>]*>\\s*<value>([^<]*)</value>"))
        {
            string key = match.Groups[1].Value;
            if (key.EndsWith("Hint", StringComparison.Ordinal)) yield return (key, match.Groups[2].Value);
        }
    }

    private static IEnumerable<string> Named(string hint)
    {
        foreach (Match match in ModifierChord.Matches(hint)) yield return match.Value;
        foreach (Match match in BracketedToolKey.Matches(hint)) yield return match.Value[1..^1];
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "NaraPainter.sln"))) return directory.FullName;
            directory = directory.Parent;
        }

        throw new InvalidOperationException($"No NaraPainter.sln found above {AppContext.BaseDirectory}.");
    }
}
