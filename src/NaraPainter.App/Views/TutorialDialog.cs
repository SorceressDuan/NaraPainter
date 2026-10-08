using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using NaraPainter.App.Services;

namespace NaraPainter.App.Views;

/// <summary>
/// The illustrated guide: chapters down the side, one step at a time beside them.
/// </summary>
/// <remarks>
/// What the steps say and which picture goes with each one is read from
/// <c>Assets/Tutorial/tutorial.json</c>, so the guide can be rewritten by dropping in files. A step
/// whose picture has not been drawn yet shows a placeholder at the size the picture is meant to be
/// rather than failing: the pictures are still being made, and the guide has to work without them.
/// </remarks>
internal sealed class TutorialDialog
{
    private readonly TutorialCatalog _catalog;
    private readonly ListView _chapters = new() { MinWidth = 140, MaxWidth = 150 };
    private readonly TextBlock _wording = new() { TextWrapping = TextWrapping.Wrap, FontSize = 15 };
    private readonly TextBlock _position = new() { Opacity = 0.7, FontSize = 12 };
    private readonly Border _picture = new() { CornerRadius = new CornerRadius(4), MinHeight = 120 };
    private readonly Button _back = new() { Content = Strings.TutorialPrevious, MinWidth = 96 };
    private readonly Button _next = new() { Content = Strings.TutorialNext, MinWidth = 96 };

    private int _chapter;
    private int _step;

    private TutorialDialog(TutorialCatalog catalog)
    {
        _catalog = catalog;
        _chapters.ItemsSource = catalog.Chapters.Select(chapter => Localization.Get(chapter.TitleKey)).ToList();
        _chapters.SelectionChanged += (_, _) => ShowChapter(_chapters.SelectedIndex);
        _back.Click += (_, _) => Step(-1);
        _next.Click += (_, _) => Step(1);
    }

    /// <summary>Shows the guide. Does nothing when the guide file could not be read.</summary>
    public static async Task ShowAsync(XamlRoot root)
    {
        TutorialCatalog? catalog = TutorialCatalog.Load();

        var dialog = new ContentDialog
        {
            Title = Strings.TutorialTitle,
            CloseButtonText = catalog is null ? Strings.TutorialClose : null,
            XamlRoot = root
        };

        if (Application.Current.Resources.TryGetValue("AppFontFamily", out object? family) && family is FontFamily appFont)
        {
            dialog.FontFamily = appFont;
        }

        if (catalog is null || catalog.Chapters.Count == 0)
        {
            dialog.Content = new TextBlock
            {
                Text = Strings.TutorialUnavailable,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 420
            };
            await dialog.ShowAsync();
            return;
        }

        var guide = new TutorialDialog(catalog);
        dialog.Content = guide.Layout(root);

        // A dialog is centred on the window, so a guide wider than the window runs off the right-hand
        // side and its buttons cannot be reached. The window is the limit, whatever it has been
        // resized to.
        if (dialog.Content is FrameworkElement content)
        {
            content.MaxWidth = Math.Max(320, root.Size.Width - 96);
            content.MaxHeight = Math.Max(320, root.Size.Height - 96);
        }

        guide.ShowChapter(0);
        await dialog.ShowAsync();
    }

    private Grid Layout(XamlRoot root)
    {
        // Sizes are kept modest on purpose: the window this opens over is often only a thousand pixels
        // wide, and a dialog wider than that has its right-hand side cut off.
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        Grid.SetColumn(_chapters, 0);
        grid.Children.Add(_chapters);

        var right = new StackPanel { Spacing = 8, MinWidth = 240 };
        right.Children.Add(_position);
        right.Children.Add(_wording);
        right.Children.Add(new ScrollViewer
        {
            Content = _picture,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            MaxHeight = 260
        });

        var footer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        footer.Children.Add(_back);
        footer.Children.Add(_next);
        right.Children.Add(footer);

        Grid.SetColumn(right, 1);
        grid.Children.Add(right);
        return grid;
    }

    /// <summary>
    /// Walks one step, carrying on into the next chapter at the end of this one. Reading the guide
    /// straight through is the common case, so the chapters should not have to be picked by hand.
    /// </summary>
    private void Step(int delta)
    {
        int wanted = _step + delta;
        if (wanted >= 0 && wanted < _catalog.Chapters[_chapter].Steps.Count)
        {
            ShowStep(wanted);
            return;
        }

        int chapter = _chapter + delta;
        if (chapter < 0 || chapter >= _catalog.Chapters.Count) return;

        _chapter = chapter;
        _chapters.SelectedIndex = chapter;
        ShowStep(delta > 0 ? 0 : _catalog.Chapters[chapter].Steps.Count - 1);
    }

    private void ShowChapter(int index)
    {
        if (index < 0 || index >= _catalog.Chapters.Count) return;

        _chapter = index;
        _chapters.SelectedIndex = index;
        ShowStep(0);
    }

    private void ShowStep(int index)
    {
        IReadOnlyList<TutorialStep> steps = _catalog.Chapters[_chapter].Steps;
        if (steps.Count == 0) return;

        _step = Math.Clamp(index, 0, steps.Count - 1);
        TutorialStep step = steps[_step];

        // The wording is a resource key, so the same guide reads in whichever language is showing.
        _wording.Text = Localization.Get(step.TextKey);
        _position.Text = Localization.Interpolate(Strings.TutorialStepFormat, _step + 1, steps.Count);

        ShowPicture(step);

        _back.IsEnabled = _step > 0 || _chapter > 0;
        _next.IsEnabled = _step < steps.Count - 1 || _chapter < _catalog.Chapters.Count - 1;
    }

    /// <summary>
    /// The step's picture, or a placeholder shaped like it when the file is not there yet.
    /// </summary>
    private void ShowPicture(TutorialStep step)
    {
        string path = _catalog.PathOf(step);
        if (File.Exists(path))
        {
            _picture.Background = null;
            _picture.BorderThickness = new Thickness(0);
            _picture.Child = new Image
            {
                Source = new BitmapImage(new Uri(path)),
                Stretch = Stretch.Uniform
            };
            return;
        }

        // Nothing to draw: an outline saying what belongs here. It is scaled down to fit rather than
        // drawn at the picture's full size, which would push the dialog wider than the window.
        _picture.Child = null;
        _picture.MinWidth = 0;
        _picture.MinHeight = Math.Min(step.ImageHeight, 160);
        _picture.BorderThickness = new Thickness(1);
        _picture.BorderBrush = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"];
        _picture.Background = (Brush)Application.Current.Resources["ControlFillColorSecondaryBrush"];
        _picture.Child = new TextBlock
        {
            Text = Localization.Interpolate(Strings.TutorialImageMissingFormat, step.ImageWidth, step.ImageHeight),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0.7
        };
    }
}
