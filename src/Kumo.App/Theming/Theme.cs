using System.Windows;
using System.Windows.Media;
using Kumo.Core.Settings;
using Microsoft.Win32;

namespace Kumo.App.Theming;

internal sealed record Palette(
    Brush Surface,
    Brush SurfaceBorder,
    Brush Segment,
    Brush SegmentHover,
    Brush SegmentSelected,
    Brush SegmentDisabled,
    Brush Text,
    Brush SelectedText,
    Brush MutedText,
    Brush Focus,
    Brush Progress,
    Brush ProgressTrack,
    Color Shadow,
    bool IsHighContrast);

internal static class Theme
{
    private const string PersonalizeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private static readonly Palette Light = new(
        Surface: Frozen(0xF7, 0xFB, 0xF8, 0xF4),
        SurfaceBorder: Frozen(0xFF, 0xE4, 0xDC, 0xD2),
        Segment: Frozen(0xFF, 0xF0, 0xEA, 0xE2),
        SegmentHover: Frozen(0xFF, 0xFB, 0xDF, 0xC6),
        SegmentSelected: Frozen(0xFF, 0xEC, 0x80, 0x34),
        SegmentDisabled: Frozen(0xFF, 0xF4, 0xF1, 0xEC),
        Text: Frozen(0xFF, 0x2B, 0x24, 0x20),
        SelectedText: Frozen(0xFF, 0x1F, 0x14, 0x0C),
        MutedText: Frozen(0xFF, 0x6B, 0x5F, 0x57),
        Focus: Frozen(0xFF, 0x2B, 0x24, 0x20),
        Progress: Frozen(0xFF, 0xE0, 0x6E, 0x22),
        ProgressTrack: Frozen(0xFF, 0xEC, 0xE5, 0xDC),
        Shadow: Color.FromArgb(0x40, 0x3A, 0x2A, 0x20),
        IsHighContrast: false);

    private static readonly Palette Dark = new(
        Surface: Frozen(0xF5, 0x26, 0x23, 0x21),
        SurfaceBorder: Frozen(0xFF, 0x3E, 0x38, 0x34),
        Segment: Frozen(0xFF, 0x33, 0x2E, 0x2B),
        SegmentHover: Frozen(0xFF, 0x4B, 0x3B, 0x2F),
        SegmentSelected: Frozen(0xFF, 0xF2, 0x8A, 0x30),
        SegmentDisabled: Frozen(0xFF, 0x2C, 0x29, 0x27),
        Text: Frozen(0xFF, 0xF4, 0xEE, 0xE8),
        SelectedText: Frozen(0xFF, 0x1F, 0x14, 0x0C),
        MutedText: Frozen(0xFF, 0xB4, 0xA8, 0x9E),
        Focus: Frozen(0xFF, 0xF4, 0xEE, 0xE8),
        Progress: Frozen(0xFF, 0xF2, 0x8A, 0x30),
        ProgressTrack: Frozen(0xFF, 0x3E, 0x38, 0x34),
        Shadow: Color.FromArgb(0x70, 0x00, 0x00, 0x00),
        IsHighContrast: false);

    public static bool PrefersDark(ThemePreference preference) => preference switch
    {
        ThemePreference.Dark => true,
        ThemePreference.Light => false,
        _ => SystemUsesDarkApps(),
    };

    /// <summary>High contrast always wins over the Kumo palette and maps onto the user's system colours.</summary>
    public static Palette Resolve(ThemePreference preference)
    {
        if (SystemParameters.HighContrast)
        {
            return new Palette(
                Surface: SystemColors.WindowBrush,
                SurfaceBorder: SystemColors.WindowTextBrush,
                Segment: SystemColors.WindowBrush,
                SegmentHover: SystemColors.HighlightBrush,
                SegmentSelected: SystemColors.HighlightBrush,
                SegmentDisabled: SystemColors.WindowBrush,
                Text: SystemColors.WindowTextBrush,
                SelectedText: SystemColors.HighlightTextBrush,
                MutedText: SystemColors.GrayTextBrush,
                Focus: SystemColors.WindowTextBrush,
                Progress: SystemColors.HighlightBrush,
                ProgressTrack: SystemColors.WindowBrush,
                Shadow: Colors.Transparent,
                IsHighContrast: true);
        }
        return PrefersDark(preference) ? Dark : Light;
    }

    public static bool ReducedMotion(MotionPreference preference) => preference switch
    {
        MotionPreference.Reduced => true,
        MotionPreference.Full => false,
        _ => !SystemParameters.ClientAreaAnimation,
    };

    public static ThemeMode WindowThemeMode(ThemePreference preference) => preference switch
    {
        ThemePreference.Light => ThemeMode.Light,
        ThemePreference.Dark => ThemeMode.Dark,
        _ => ThemeMode.System,
    };

    private static bool SystemUsesDarkApps()
    {
        using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKey);
        return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
    }

    private static SolidColorBrush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }
}
