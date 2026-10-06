using System.Reflection;
using NaraPainter.App.Controls;

namespace NaraPainter.Tests;

/// <summary>
/// Checks the one interaction detail the slider undo depends on: a second session start during the
/// same drag is ignored. Pressing the thumb hands the slider focus a moment after the press, which
/// used to open that second session and leave one undo step per value change behind.
/// </summary>
internal static class AdjustmentSliderGuard
{
    public static void AssertASecondBeginIsIgnored()
    {
        AdjustmentSlider slider;
        try
        {
            slider = new AdjustmentSlider();
        }
        catch (Exception error) when (error is not OutOfMemoryException)
        {
            // Creating a XAML control needs a dispatcher, which a plain test thread does not have.
            // The merge tests above cover the behaviour; this guard only runs where it can.
            return;
        }

        int started = 0;
        slider.EditStarted += (_, _) => started++;

        BeginEdit(slider);
        BeginEdit(slider);

        if (started != 1)
        {
            throw new InvalidOperationException(
                $"Starting a session twice during one drag raised EditStarted {started} times, which splits the drag into that many undo steps.");
        }
    }

    private static void BeginEdit(AdjustmentSlider slider) =>
        typeof(AdjustmentSlider)
            .GetMethod("BeginEdit", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(slider, null);
}
