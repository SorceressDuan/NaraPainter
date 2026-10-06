using System.Text;
using NaraDreamPainter.Models.Layers;

namespace NaraDreamPainter.App.ViewModels;

/// <summary>
/// Display names for the 24 modes, one per enum value in enum order. The blend picker binds its
/// SelectedIndex straight to the mode, so the list order has to stay the enum order.
/// </summary>
public static class BlendModeCatalog
{
    public static IReadOnlyList<string> Names { get; } = Enum.GetNames<BlendMode>().Select(Space).ToArray();

    private static string Space(string name)
    {
        var builder = new StringBuilder(name.Length + 4);
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i])) builder.Append(' ');
            builder.Append(name[i]);
        }
        return builder.ToString();
    }
}
