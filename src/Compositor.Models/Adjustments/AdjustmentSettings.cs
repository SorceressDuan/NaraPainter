namespace Compositor.Models.Adjustments;

/// <summary>
/// An adjustment layer's recipe. Concrete settings objects are immutable snapshots so undo can
/// hold on to the previous value without cloning.
/// </summary>
public abstract class AdjustmentSettings
{
    /// <summary>Name shown in the layers panel and the adjustment picker.</summary>
    public abstract string DisplayName { get; }

    public abstract AdjustmentSettings Clone();
}
