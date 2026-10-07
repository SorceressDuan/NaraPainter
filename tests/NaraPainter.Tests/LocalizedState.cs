using Xunit;

namespace NaraPainter.Tests;

/// <summary>
/// Marks the tests that read or change the process-wide interface language.
/// </summary>
/// <remarks>
/// <see cref="App.Localization"/> keeps the active culture in a static field, so a test that switches
/// it - or one that compares a localized string it read a moment earlier - is only correct while
/// nothing else changes the language underneath it. xUnit runs test classes in parallel by default,
/// which is exactly that situation: a switch in the middle of another class's assertion turns an
/// expected "Paint Mask" into "涂抹蒙版". Tests in one collection run in sequence, so everything that
/// touches the language belongs to this one.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalizedState
{
    public const string Name = "localized state";
}
