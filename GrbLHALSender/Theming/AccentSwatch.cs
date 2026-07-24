using Avalonia.Media;
using System.Collections.Generic;

namespace GrbLHALSender.Theming;

/// <summary>
/// One tappable accent choice in the Theme tab. Kept as a fixed set of vetted
/// colors rather than a free colour wheel: every one of these has been picked to
/// stay legible against the slate surfaces on a shop-floor screen.
/// </summary>
public sealed class AccentSwatch
{
    public AccentSwatch(string name, string hex)
    {
        Name = name;
        Hex = hex;
        Brush = new SolidColorBrush(Color.Parse(hex));
    }

    public string Name { get; }
    public string Hex { get; }
    public IBrush Brush { get; }

    public static IReadOnlyList<AccentSwatch> Defaults { get; } =
    [
        new("Azure", "#007ACC"),
        new("Sky", "#2D9CDB"),
        new("Teal", "#00B4A6"),
        new("Green", "#3DBE6E"),
        new("Lime", "#8BC34A"),
        new("Amber", "#E8A02E"),
        new("Orange", "#F2762E"),
        new("Red", "#E5484D"),
        new("Pink", "#E0559B"),
        new("Violet", "#A166F0"),
        new("Indigo", "#6C7BF0"),
        new("Steel", "#9AA5B1"),
    ];
}
