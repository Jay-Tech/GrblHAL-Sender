using Avalonia.Media;
using System;
using System.Collections.Generic;
using System.Linq;

namespace GrbLHALSender.Theming;

/// <summary>
/// The complete set of semantic colors the UI paints with. Views never name a raw
/// hex value — they bind to the resource keys <see cref="ThemeService"/> generates
/// from one of these.
///
/// A preset only defines base colors; hover/pressed/disabled variants and the
/// Fluent accent ramp are derived in <see cref="ThemeService"/>, so changing the
/// accent is a single input rather than a dozen coordinated edits.
/// </summary>
public sealed record ThemePalette
{
    public required string Id { get; init; }
    public required string DisplayName { get; init; }

    /// <summary>
    /// True for palettes with a light page. Drives the Fluent theme variant (so
    /// built-in controls flip their own baseline) and the direction of derived
    /// hover/pressed tints, which must darken on light and lighten on dark.
    /// </summary>
    public bool IsLight { get; init; }

    // Surfaces, back to front.
    public required Color AppBackground { get; init; }
    public required Color Surface { get; init; }
    public required Color SurfaceAlt { get; init; }
    public required Color SurfaceHeader { get; init; }
    public required Color PanelBorder { get; init; }

    // Text.
    public required Color TextPrimary { get; init; }
    public required Color TextMuted { get; init; }

    // Accent and state colors.
    public required Color Accent { get; init; }
    public required Color Alert { get; init; }
    public required Color Warn { get; init; }
    public required Color Ok { get; init; }

    /// <summary>
    /// Fill for an untriggered signal LED. Deliberately lighter than
    /// <see cref="PanelBorder"/> so the dots stay readable against the header on a
    /// glare-prone shop screen.
    /// </summary>
    public required Color LedIdle { get; init; }

    /// <summary>The 3D toolpath viewport clears to this.</summary>
    public required Color ViewportBackground { get; init; }

    /// <summary>
    /// The preset's own accent, kept separate from <see cref="Accent"/> so a user
    /// override can always be reset without re-looking-up the preset.
    /// </summary>
    public Color DefaultAccent { get; init; }
}

/// <summary>
/// Built-in themes.
///
/// TO ADD A THEME: copy one of the palettes below, give it a unique <c>Id</c> (the
/// value written to the config file — never rename an existing one, or saved configs
/// silently fall back to the default) and a <c>DisplayName</c> (what the dropdown
/// shows), then add it to <see cref="All"/>. That is the whole job: the Theme tab
/// enumerates this list, and every view paints from the resource keys
/// <see cref="ThemeService"/> derives from it, so no XAML changes are needed.
///
/// Set <c>IsLight</c> for any palette with a light page. Hover/pressed tints and the
/// Fluent theme variant follow from it.
/// </summary>
public static class ThemePresets
{
    /// <summary>
    /// The palette the Reference (cheat sheet) tab was built with, promoted to the
    /// whole application: near-black slate page, lifted panels, hairline borders.
    /// </summary>
    public static readonly ThemePalette SlateDark = new()
    {
        Id = "SlateDark",
        DisplayName = "Slate Dark",
        AppBackground = Color.Parse("#16191D"),
        Surface = Color.Parse("#20252B"),
        SurfaceAlt = Color.Parse("#252A31"),
        SurfaceHeader = Color.Parse("#1B1F24"),
        PanelBorder = Color.Parse("#2C3238"),
        TextPrimary = Color.Parse("#E4E8EB"),
        TextMuted = Color.Parse("#98A2AC"),
        // Amber, matching the code/section color the Reference tab already uses,
        // so the shell and that page read as one design rather than two.
        Accent = Color.Parse("#E8A02E"),
        DefaultAccent = Color.Parse("#E8A02E"),
        Alert = Color.Parse("#FF5B5B"),
        Warn = Color.Parse("#E8A02E"),
        Ok = Color.Parse("#5BC98A"),
        LedIdle = Color.Parse("#454E58"),
        ViewportBackground = Color.Parse("#191922"),
    };

    /// <summary>
    /// The colors the app shipped with before the theme system: #282828 page,
    /// #2D2D30 panels, #007ACC blue. Kept so the familiar look is one dropdown
    /// entry away rather than a git checkout.
    /// </summary>
    public static readonly ThemePalette Classic = new()
    {
        Id = "Classic",
        DisplayName = "Classic",
        AppBackground = Color.Parse("#282828"),
        Surface = Color.Parse("#2D2D30"),
        SurfaceAlt = Color.Parse("#3E3E42"),
        SurfaceHeader = Color.Parse("#252528"),
        PanelBorder = Color.Parse("#3F3F46"),
        TextPrimary = Color.Parse("#E8E8E8"),
        TextMuted = Color.Parse("#CCCCCC"),
        Accent = Color.Parse("#007ACC"),
        DefaultAccent = Color.Parse("#007ACC"),
        Alert = Color.Parse("#FF3333"),
        Warn = Color.Parse("#E8A02E"),
        Ok = Color.Parse("#98FB98"),
        LedIdle = Color.Parse("#505050"),
        ViewportBackground = Color.Parse("#19191E"),
    };

    /// <summary>
    /// For well-lit shops and daylight-readable panels. Note the viewport stays
    /// dark: the toolpath's rapid/cut/completed colors are tuned for a dark canvas,
    /// and washing them out mid-job is not a trade worth making for consistency.
    /// </summary>
    public static readonly ThemePalette ShopLight = new()
    {
        Id = "ShopLight",
        DisplayName = "Shop Light",
        IsLight = true,
        AppBackground = Color.Parse("#EFF2F6"),
        Surface = Color.Parse("#FFFFFF"),
        SurfaceAlt = Color.Parse("#E4E9F0"),
        SurfaceHeader = Color.Parse("#DFE5ED"),
        PanelBorder = Color.Parse("#C2CCD8"),
        TextPrimary = Color.Parse("#1A1F27"),
        TextMuted = Color.Parse("#5A6673"),
        // Darker than the dark themes' accent so it holds contrast on white.
        Accent = Color.Parse("#0A6FB8"),
        DefaultAccent = Color.Parse("#0A6FB8"),
        Alert = Color.Parse("#C62828"),
        Warn = Color.Parse("#A76400"),
        Ok = Color.Parse("#2E7D32"),
        LedIdle = Color.Parse("#AEB9C6"),
        ViewportBackground = Color.Parse("#191922"),
    };

    /// <summary>Order here is the order shown in the Theme tab's dropdown.</summary>
    public static IReadOnlyList<ThemePalette> All { get; } = [Classic, SlateDark, ShopLight];

    public static ThemePalette Default => Classic;

    public static ThemePalette ById(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? Default;
}
