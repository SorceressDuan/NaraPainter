using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;

namespace NaraDreamPainter.App.Controls;

/// <summary>
/// Caption, number box and slider for one numeric property. EditStarted fires when an interaction
/// begins so the view model can open a new undo merge key, which keeps a whole drag to one step.
/// </summary>
public sealed partial class AdjustmentSlider : UserControl
{
    public static readonly DependencyProperty HeaderProperty = DependencyProperty.Register(
        nameof(Header), typeof(string), typeof(AdjustmentSlider), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty MinimumProperty = DependencyProperty.Register(
        nameof(Minimum), typeof(double), typeof(AdjustmentSlider), new PropertyMetadata(0d, OnRangeChanged));

    public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
        nameof(Maximum), typeof(double), typeof(AdjustmentSlider), new PropertyMetadata(100d, OnRangeChanged));

    public static readonly DependencyProperty StepProperty = DependencyProperty.Register(
        nameof(Step), typeof(double), typeof(AdjustmentSlider), new PropertyMetadata(1d, OnRangeChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(AdjustmentSlider), new PropertyMetadata(0d, OnValueChanged));

    private bool _syncing;
    private bool _editing;

    public AdjustmentSlider()
    {
        InitializeComponent();

        Track.ValueChanged += OnTrackValueChanged;
        Track.PointerPressed += (_, _) => BeginEdit();
        Track.PointerReleased += (_, _) => EndEdit();
        Track.PointerCaptureLost += (_, _) => EndEdit();
        Track.GotFocus += (_, _) => BeginEdit();
        Track.LostFocus += (_, _) => EndEdit();

        Box.ValueChanged += OnBoxValueChanged;
        Box.GotFocus += (_, _) => BeginEdit();
        Box.LostFocus += (_, _) => EndEdit();

        ApplyRange();
        Sync(Value);
    }

    public event EventHandler? EditStarted;

    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Step
    {
        get => (double)GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    private static void OnRangeChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((AdjustmentSlider)sender).ApplyRange();

    private static void OnValueChanged(DependencyObject sender, DependencyPropertyChangedEventArgs args) =>
        ((AdjustmentSlider)sender).Sync((double)args.NewValue);

    private void OnTrackValueChanged(object sender, RangeBaseValueChangedEventArgs args)
    {
        if (_syncing) return;
        Assign(args.NewValue);
    }

    private void OnBoxValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_syncing || double.IsNaN(args.NewValue)) return;
        Assign(Math.Round(args.NewValue, 3));
    }

    /// <summary>
    /// A value arriving with no pointer or focus event in front of it is a single edit of its own:
    /// open and close a session around it so it does not merge into the previous one.
    /// </summary>
    private void Assign(double value)
    {
        bool standalone = !_editing;
        if (standalone) BeginEdit();

        Value = value;

        if (standalone) EndEdit();
    }

    private void BeginEdit()
    {
        if (_editing) return;

        _editing = true;
        EditStarted?.Invoke(this, EventArgs.Empty);
    }

    private void EndEdit() => _editing = false;

    private void ApplyRange()
    {
        double minimum = Minimum;
        double maximum = Math.Max(Maximum, minimum + 0.01);
        double step = Step > 0 ? Step : 0.01;

        Track.Minimum = minimum;
        Track.Maximum = maximum;
        Track.StepFrequency = step;

        Box.Minimum = minimum;
        Box.Maximum = maximum;
        Box.SmallChange = step;
        Box.LargeChange = step * 10;
    }

    private void Sync(double value)
    {
        _syncing = true;
        try
        {
            if (Math.Abs(Track.Value - value) > 1e-9) Track.Value = value;
            if (double.IsNaN(Box.Value) || Math.Abs(Box.Value - value) > 1e-9) Box.Value = value;
        }
        finally
        {
            _syncing = false;
        }
    }
}
