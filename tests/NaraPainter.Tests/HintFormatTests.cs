using System.Text.RegularExpressions;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// Every tooltip reads the same way: what the control does, then its gesture in brackets.
/// </summary>
/// <remarks>
/// The wording drifted apart once already. Export answered with a description, the zoom buttons
/// answered with a description and no key, and the transform and filter buttons answered with
/// nothing but a list of keys — those two were hard-coded shortcut strings with no wording at all,
/// which is why one tooltip read "Ctrl+Shift+B, Ctrl+Shift+U" and nothing else.
/// </remarks>
public class HintFormatTests
{
    /// <summary>A gesture: a modifier run ending in a key, or one of the bare tool letters.</summary>
    private static readonly Regex Gesture = new(
        @"\b(?:Ctrl|Alt|Shift)(?:\+(?:Ctrl|Alt|Shift))*\+(?:\+\+|[-0-9A-Z])\b|(?<!\w)[MIJ](?!\w)|F1",
        RegexOptions.Compiled);

    /// <summary>A hint that is nothing but gestures: no wording in front of the bracket.</summary>
    private static readonly Regex GestureOnly = new(
        @"^(?:\(?[^()]*\)?)$",
        RegexOptions.Compiled);

    [Fact]
    public void NoTooltipIsOnlyAShortcut()
    {
        // The shape that reached the user: a button whose tooltip names the keys and never says what
        // it does, so somebody who does not already know the feature learns nothing from hovering.
        string[] offenders = [.. Hints()
            .Where(hint => Gesture.IsMatch(hint.Value))
            .Where(hint => !HasWording(hint.Value))
            .Select(hint => $"{hint.Key} = {hint.Value}")];

        Assert.True(offenders.Length == 0,
            "A tooltip names keys without saying what the control does:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void EveryShortcutInATooltipIsBracketed()
    {
        // "(Ctrl+E)" and not "Ctrl+E": the bracket is what marks it as a key rather than more prose.
        string[] offenders = [.. Hints()
            .Where(hint => Gesture.IsMatch(hint.Value))
            .Where(hint => !Regex.IsMatch(hint.Value, @"[（(][^）)]*(?:Ctrl|Alt|Shift|F1|(?<!\w)[MIJ](?!\w))"))
            .Select(hint => $"{hint.Key} = {hint.Value}")];

        Assert.True(offenders.Length == 0,
            "A tooltip names a key outside brackets:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void EveryToolbarButtonThatHasAGestureSaysSo()
    {
        // The other direction: a toolbar button bound in the shortcut table should not hide its key.
        var bound = BoundGestures();
        string[] offenders = [.. ToolbarHints()
            .Where(hint => hint.Gesture is not null && bound.Contains(hint.Gesture))
            .Where(hint => !hint.Value.Contains(hint.Gesture!, StringComparison.Ordinal))
            .Select(hint => $"{hint.Key} is bound to {hint.Gesture} but does not name it")];

        Assert.True(offenders.Length == 0,
            "A toolbar tooltip hides a gesture the window binds:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    /// <summary>True when something other than a gesture sits in the hint.</summary>
    private static bool HasWording(string hint)
    {
        // Take the gestures out, then whatever separators and brackets are left over, and see whether
        // any word remains. "Ctrl+Shift+B, Ctrl+Shift+U" leaves nothing; "高斯模糊与 USM 锐化
        // (Ctrl+Shift+B / Ctrl+Shift+U)" leaves the words.
        string rest = Gesture.Replace(hint, " ")
            .Replace("(", " ").Replace(")", " ")
            .Replace("（", " ").Replace("）", " ")
            .Replace("/", " ").Replace(",", " ").Replace("，", " ").Replace("、", " ");

        return Regex.IsMatch(rest, @"\p{L}|\p{Nd}");
    }

    private static IEnumerable<string> Named(string hint) =>
        Gesture.Matches(hint).Select(match => match.Value);

    /// <summary>
    /// The toolbar's own hints, paired with the gesture the button is bound to, so the check can tell
    /// a button that has a key from one that never had one.
    /// </summary>
    private static IEnumerable<(string Key, string? Gesture, string Value)> ToolbarHints()
    {
        (string Key, string? Gesture)[] pairs =
        [
            ("Toolbar_OpenHint", "Ctrl+O"),
            ("Toolbar_ExportHint", "Ctrl+S"),
            ("Toolbar_UndoHint", "Ctrl+Z"),
            ("Toolbar_RedoHint", "Ctrl+Y"),
            ("Toolbar_NewLayerHint", "Ctrl+N"),
            ("Toolbar_DuplicateHint", "Ctrl+J"),
            ("Toolbar_ZoomOutHint", "Ctrl+-"),
            ("Toolbar_ZoomInHint", "Ctrl++"),
            ("Toolbar_FitHint", "Ctrl+0"),
            ("Toolbar_ActualSizeHint", "Ctrl+1"),
            ("Toolbar_MaskBrushHint", "M"),
            ("Toolbar_SpotHealHint", "J"),
            ("Toolbar_TextHint", "Ctrl+T"),
            ("Toolbar_ColorPickerHint", "I"),
        ];

        var hints = Hints().ToDictionary(hint => hint.Key, hint => hint.Value, StringComparer.Ordinal);
        foreach ((string key, string? gesture) in pairs)
        {
            if (hints.TryGetValue(key, out string? value)) yield return (key, gesture, value);
        }
    }

    private static IEnumerable<(string Key, string Value)> Hints()
    {
        string path = Path.Combine(Root(), "src", "NaraPainter.App", "Resources", "Strings.zh-CN.resx");
        foreach (Match match in Regex.Matches(
            TestFiles.ReadText(path), "<data name=\"([^\"]+)\"[^>]*>\\s*<value>([^<]*)</value>"))
        {
            string key = match.Groups[1].Value;
            if (key.EndsWith("Hint", StringComparison.Ordinal)) yield return (key, match.Groups[2].Value);
        }
    }

    private static HashSet<string> BoundGestures()
    {
        string source = TestFiles.ReadText(
            Path.Combine(Root(), "src", "NaraPainter.App", "Views", "MainWindow.xaml.cs"));
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

    private static string Root()
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
