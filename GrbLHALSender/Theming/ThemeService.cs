using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using GrbLHALSender.Configuration;
using System;

namespace GrbLHALSender.Theming;

/// <summary>
/// Owns the live application palette. Builds a ResourceDictionary from a
/// <see cref="ThemePalette"/> and installs it as a merged dictionary on
/// Application.Resources.
///
/// Why a merged dictionary rather than editing the FluentTheme palette:
/// Application.TryGetResource searches Resources before Styles, so these keys win
/// over the theme's, and *replacing* a merged dictionary raises ResourcesChanged —
/// which is what makes every DynamicResource consumer repaint without a restart.
/// </summary>
public sealed class ThemeService
{
    private ResourceDictionary? _installed;

    /// <summary>
    /// The palette currently in effect. Static so the Skia/OpenGL renderers, which
    /// draw outside the resource system, can read the viewport color at draw time.
    /// </summary>
    public static ThemePalette Current { get; private set; } = ThemePresets.Default;

    public ThemeService(ConfigManager configManager)
    {
        // The config lifecycle already broadcasts these; hooking them means the theme
        // applies on startup and re-applies on every save with no extra wiring.
        configManager.OnConfigLoaded += (_, cfg) => Apply(cfg.Theme);
        configManager.OnConfigSaved += (_, cfg) => Apply(cfg.Theme);
    }

    /// <summary>Resolves a persisted config to a concrete palette, applying any accent override.</summary>
    public static ThemePalette Resolve(ThemeConfig? config)
    {
        var palette = ThemePresets.ById(config?.Preset);
        return TryParseColor(config?.AccentColor, out var accent)
            ? palette with { Accent = accent }
            : palette;
    }

    public void Apply(ThemeConfig? config) => Apply(Resolve(config));

    public void Apply(ThemePalette palette)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => Apply(palette));
            return;
        }

        Current = palette;

        if (Application.Current is not { } app) return;

        // Flip the Fluent variant first so the built-in control themes rebase to a
        // light or dark baseline; our own keys are layered on top of that below.
        app.RequestedThemeVariant = palette.IsLight ? ThemeVariant.Light : ThemeVariant.Dark;

        var dictionary = Build(palette);
        var previous = _installed;
        _installed = dictionary;

        app.Resources.MergedDictionaries.Add(dictionary);
        if (previous is not null)
            app.Resources.MergedDictionaries.Remove(previous);
    }

    /// <summary>
    /// Parses "#RRGGBB"/"#AARRGGBB". Returns false for null, blank or malformed input
    /// so a hand-edited config file can never leave the app unstyled.
    /// </summary>
    public static bool TryParseColor(string? value, out Color color)
    {
        color = default;
        return !string.IsNullOrWhiteSpace(value) && Color.TryParse(value, out color);
    }

    private static ResourceDictionary Build(ThemePalette p)
    {
        var d = new ResourceDictionary();

        void Brush(string key, Color c) => d[key] = new SolidColorBrush(c);
        void Col(string key, Color c) => d[key] = c;

        // On a light page, "raised" means slightly darker, not lighter. Every
        // derived hover/pressed tint goes through these so one flag flips them all.
        Color Raise(Color c, double t) => p.IsLight ? Darken(c, t) : Lighten(c, t);
        Color Sink(Color c, double t) => p.IsLight ? Lighten(c, t) : Darken(c, t);

        // --- Surfaces -------------------------------------------------------
        Brush("AppBackgroundBrush", p.AppBackground);
        Brush("SurfaceBrush", p.Surface);
        Brush("SurfaceAltBrush", p.SurfaceAlt);
        Brush("SurfaceHeaderBrush", p.SurfaceHeader);
        Brush("SurfaceHoverBrush", Raise(p.Surface, 0.08));
        Brush("PanelBorderBrush", p.PanelBorder);

        // Semi-transparent wash used by panels that float over the 3D viewport
        // (console, gcode, macro) so the toolpath stays faintly visible behind them.
        // Derived from Surface, not AppBackground, so it stays readable on a light
        // shell where the viewport underneath is still dark.
        Brush("OverlayScrimBrush", WithAlpha(p.Surface, p.IsLight ? (byte)0xF2 : (byte)0xD8));

        // --- Text -----------------------------------------------------------
        Brush("TextPrimaryBrush", p.TextPrimary);
        Brush("TextMutedBrush", p.TextMuted);
        Brush("GripIdleBrush", Darken(p.TextMuted, 0.35));

        // --- Accent + state -------------------------------------------------
        Brush("AccentBrush", p.Accent);
        Brush("AccentHoverBrush", Lighten(p.Accent, 0.18));
        Brush("AccentPressedBrush", Darken(p.Accent, 0.18));
        Brush("AccentSoftBrush", WithAlpha(p.Accent, 0x33));
        Brush("SelectionBrush", WithAlpha(p.Accent, 0x55));

        // Text drawn ON an accent/alert fill. Picked per color rather than fixed:
        // the accent is user-chosen, and light accents (amber, lime, steel) need
        // dark text while dark ones (azure, violet) need light text.
        Brush("AccentForegroundBrush", OnColor(p.Accent, p.TextPrimary));
        Brush("AlertForegroundBrush", OnColor(p.Alert, p.TextPrimary));

        Brush("AlertBrush", p.Alert);
        Brush("AlertSoftBrush", WithAlpha(p.Alert, 0x33));
        Brush("WarnBrush", p.Warn);
        Brush("WarnSoftBrush", WithAlpha(p.Warn, 0x33));
        Brush("OkBrush", p.Ok);
        Brush("LedIdleBrush", p.LedIdle);
        Brush("ViewportBackgroundBrush", p.ViewportBackground);

        // --- Raw colors, for the few consumers that need Color not Brush -----
        Col("AppBackgroundColor", p.AppBackground);
        Col("SurfaceColor", p.Surface);
        Col("PanelBorderColor", p.PanelBorder);
        Col("TextPrimaryColor", p.TextPrimary);
        Col("TextMutedColor", p.TextMuted);
        Col("AccentColor", p.Accent);
        Col("AlertColor", p.Alert);
        Col("WarnColor", p.Warn);
        Col("OkColor", p.Ok);
        Col("ViewportBackgroundColor", p.ViewportBackground);

        // --- Fluent accent ramp ---------------------------------------------
        // Overriding these steers the built-in control themes (focus rings, selected
        // list items, toggle fills) without restating every control's own keys.
        Col("SystemAccentColor", p.Accent);
        Col("SystemAccentColorLight1", Lighten(p.Accent, 0.20));
        Col("SystemAccentColorLight2", Lighten(p.Accent, 0.40));
        Col("SystemAccentColorLight3", Lighten(p.Accent, 0.60));
        Col("SystemAccentColorDark1", Darken(p.Accent, 0.20));
        Col("SystemAccentColorDark2", Darken(p.Accent, 0.40));
        Col("SystemAccentColorDark3", Darken(p.Accent, 0.60));
        Brush("SystemAccentColorBrush", p.Accent);
        Brush("TextControlSelectionHighlightColor", WithAlpha(p.Accent, 0x99));

        // --- Tab strip ------------------------------------------------------
        // Overriding Fluent's documented TabItemHeader* keys is safer than
        // restyling template parts: a renamed part fails silently, a missing
        // resource key does not.
        Brush("TabItemHeaderBackgroundSelected", p.Surface);
        Brush("TabItemHeaderBackgroundUnselected", Colors.Transparent);
        Brush("TabItemHeaderBackgroundPointerOver", Lighten(p.Surface, 0.08));
        Brush("TabItemHeaderBackgroundPressed", p.SurfaceAlt);
        Brush("TabItemHeaderForegroundSelected", p.TextPrimary);
        Brush("TabItemHeaderForegroundUnselected", p.TextMuted);
        Brush("TabItemHeaderForegroundPointerOver", p.TextPrimary);
        Brush("TabItemHeaderForegroundPressed", p.TextPrimary);
        Brush("TabItemHeaderSelectedPipeFill", p.Accent);

        // --- Built-in control chrome ----------------------------------------
        // The Fluent palettes in App.axaml only give one grey ramp per variant, so
        // without these two dark presets would share identical buttons and text
        // boxes while their panels differed. Deriving the control families here
        // keeps each preset internally consistent. An unrecognised key is simply
        // ignored by the theme, so this list is safe to extend.
        var control = p.SurfaceAlt;
        var disabledText = WithAlpha(p.TextMuted, 0x8A);

        Brush("ButtonBackground", control);
        Brush("ButtonBackgroundPointerOver", Raise(control, 0.10));
        Brush("ButtonBackgroundPressed", Sink(control, 0.10));
        Brush("ButtonBackgroundDisabled", p.Surface);
        Brush("ButtonForeground", p.TextPrimary);
        Brush("ButtonForegroundPointerOver", p.TextPrimary);
        Brush("ButtonForegroundPressed", p.TextPrimary);
        Brush("ButtonForegroundDisabled", disabledText);
        Brush("ButtonBorderBrush", p.PanelBorder);
        Brush("ButtonBorderBrushPointerOver", Raise(p.PanelBorder, 0.15));
        Brush("ButtonBorderBrushPressed", p.PanelBorder);
        Brush("ButtonBorderBrushDisabled", p.PanelBorder);

        var field = p.IsLight ? p.Surface : Sink(p.Surface, 0.25);
        Brush("TextControlBackground", field);
        Brush("TextControlBackgroundPointerOver", field);
        Brush("TextControlBackgroundFocused", field);
        Brush("TextControlBackgroundDisabled", p.Surface);
        Brush("TextControlForeground", p.TextPrimary);
        Brush("TextControlForegroundPointerOver", p.TextPrimary);
        Brush("TextControlForegroundFocused", p.TextPrimary);
        Brush("TextControlForegroundDisabled", disabledText);
        Brush("TextControlBorderBrush", p.PanelBorder);
        Brush("TextControlBorderBrushPointerOver", Raise(p.PanelBorder, 0.15));
        Brush("TextControlBorderBrushFocused", p.Accent);
        Brush("TextControlPlaceholderForeground", p.TextMuted);

        Brush("ComboBoxBackground", control);
        Brush("ComboBoxBackgroundPointerOver", Raise(control, 0.10));
        Brush("ComboBoxBackgroundPressed", Sink(control, 0.10));
        Brush("ComboBoxForeground", p.TextPrimary);
        Brush("ComboBoxBorderBrush", p.PanelBorder);
        Brush("ComboBoxDropDownBackground", p.Surface);
        Brush("ComboBoxDropDownForeground", p.TextPrimary);
        Brush("ComboBoxDropDownBorderBrush", p.PanelBorder);

        Brush("ToolTipBackground", p.SurfaceHeader);
        Brush("ToolTipForeground", p.TextPrimary);
        Brush("ToolTipBorderBrush", p.PanelBorder);

        return d;
    }

    private static Color Mix(Color from, Color to, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return Color.FromArgb(
            from.A,
            (byte)Math.Round(from.R + (to.R - from.R) * t),
            (byte)Math.Round(from.G + (to.G - from.G) * t),
            (byte)Math.Round(from.B + (to.B - from.B) * t));
    }

    private static Color Lighten(Color c, double t) => Mix(c, Colors.White, t);
    private static Color Darken(Color c, double t) => Mix(c, Colors.Black, t);
    private static Color WithAlpha(Color c, byte alpha) => Color.FromArgb(alpha, c.R, c.G, c.B);

    /// <summary>Near-black companion for text sitting on a light fill.</summary>
    private static readonly Color InkOnLight = Color.FromRgb(0x14, 0x17, 0x1B);

    /// <summary>
    /// Chooses whichever of <paramref name="light"/> or <see cref="InkOnLight"/> has
    /// the better WCAG contrast against <paramref name="background"/>. Amber at
    /// #E8A02E scores ~1.9:1 against the light text and ~8:1 against the dark, so
    /// the choice genuinely matters for readability on the machine.
    /// </summary>
    private static Color OnColor(Color background, Color light) =>
        ContrastRatio(background, light) >= ContrastRatio(background, InkOnLight)
            ? light
            : InkOnLight;

    private static double ContrastRatio(Color a, Color b)
    {
        var la = RelativeLuminance(a);
        var lb = RelativeLuminance(b);
        var (hi, lo) = la > lb ? (la, lb) : (lb, la);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double RelativeLuminance(Color c)
    {
        static double Channel(byte v)
        {
            var s = v / 255.0;
            return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }

        return 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);
    }
}
