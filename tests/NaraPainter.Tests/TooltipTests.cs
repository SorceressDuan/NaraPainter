using System.Text.RegularExpressions;
using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// Guards the rule that made the tooltips look unreliable: a control that is disabled never shows one.
/// </summary>
/// <remarks>
/// WinUI opens a tooltip from pointer messages, and a disabled control receives none, so
/// <c>ToolTipService</c> on a greyed-out button never fires. Hovering the toolbar on a fresh document
/// showed it plainly: Open and Export answered, Undo and Redo stayed silent because there was nothing
/// to undo. The buttons that can go unavailable therefore fade instead of disabling, and their
/// handlers already return without doing anything when the action is not available.
/// </remarks>
public class TooltipTests
{
    [Fact]
    public void NoButtonThatGoesUnavailableIsDisabled()
    {
        // A button that both carries a tooltip and binds IsEnabled to a document flag is the shape
        // that loses its hint. Buttons that stay enabled may keep IsEnabled; none currently do.
        string[] offenders = [.. SourceFiles()
            .SelectMany(file => Elements(TestFiles.ReadText(file)).Select(element => (file, element)))
            .Where(hit => hit.element.Contains("ToolTipService.ToolTip", StringComparison.Ordinal)
                && hit.element.Contains("IsEnabled", StringComparison.Ordinal))
            .Select(hit => $"{Path.GetFileName(hit.file)}: {FirstLine(hit.element)}")];

        Assert.True(offenders.Length == 0,
            "A button that can be disabled will never show its tooltip:" + Environment.NewLine
            + string.Join(Environment.NewLine, offenders));
    }

    [Fact]
    public void TheButtonsThatGoUnavailableFadeInstead()
    {
        // The other half of the rule: they still have to look unavailable some other way.
        string main = TestFiles.ReadText(Path.Combine(Root(), "src", "NaraPainter.App", "Views", "MainWindow.xaml"));
        string panel = TestFiles.ReadText(Path.Combine(Root(), "src", "NaraPainter.App", "Views", "LayersPanel.xaml"));

        Assert.Contains("EnabledToOpacity", main, StringComparison.Ordinal);
        Assert.Contains("EnabledToOpacity", panel, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"{x:Bind Document.", main, StringComparison.Ordinal);
        Assert.DoesNotContain("IsEnabled=\"{Binding HasSelection}\"", panel, StringComparison.Ordinal);
    }

    /// <summary>Splits a XAML document into its elements, one string each.</summary>
    private static IEnumerable<string> Elements(string xaml)
    {
        // An element runs from its opening tag to whichever comes first: a self-closing "/>" or the
        // line closing it. Multi-line is expected — an AppBarButton is usually written across several
        // lines — so the body is allowed to cross lines but has to stop at the first terminator.
        foreach (Match match in Regex.Matches(
            xaml,
            @"^[ \t]*<(?<name>[A-Za-z:]+)\b.*?(?:/>|</\k<name>>)[ \t]*$",
            RegexOptions.Multiline | RegexOptions.Singleline))
        {
            yield return match.Value;
        }
    }

    private static string FirstLine(string element) =>
        element.Split('\n')[0].Trim();

    private static IEnumerable<string> SourceFiles()
    {
        string views = Path.Combine(Root(), "src", "NaraPainter.App", "Views");
        return Directory.EnumerateFiles(views, "*.xaml");
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
