using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SonnyTray;

public enum AppThemeMode { System, Dark, Light }

public static class ThemeManager
{
    private static AppThemeMode _currentMode = AppThemeMode.Dark;
    private static bool _listening;

    public static AppThemeMode CurrentMode => _currentMode;

    public static event Action? ThemeChanged;

    public static void ApplyTheme(AppThemeMode mode)
    {
        _currentMode = mode;
        var resolved = mode == AppThemeMode.System ? GetSystemTheme() : mode.ToString();
        ApplyResources(resolved);

        // Always listen so we can pick up accent color changes too
        if (!_listening)
        {
            _listening = true;
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        }
    }

    public static void ApplyTheme(string theme)
    {
        _currentMode = theme == "Dark" ? AppThemeMode.Dark : AppThemeMode.Light;
        ApplyResources(theme);

        if (!_listening)
        {
            _listening = true;
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        }
    }

    private static void ApplyResources(string theme)
    {
        var dict = new ResourceDictionary
        {
            Source = new Uri($"pack://application:,,,/Themes/{theme}Theme.xaml", UriKind.Absolute)
        };

        var app = Application.Current;
        app.Resources.MergedDictionaries.Clear();
        app.Resources.MergedDictionaries.Add(dict);

        ApplySystemAccentColor(app.Resources);

        ThemeChanged?.Invoke();
    }

    private static void ApplySystemAccentColor(ResourceDictionary resources)
    {
        var isDark = resources["TextFillColorPrimary"] is Color c && c.R > 200;
        var accent = GetSystemAccentFromPalette(isDark);
        var accentSecondary = Color.FromArgb(0xCC, accent.R, accent.G, accent.B);
        var accentTertiary = Color.FromArgb(0x99, accent.R, accent.G, accent.B);

        resources["AccentDefault"] = accent;
        resources["AccentSecondary"] = accentSecondary;
        resources["AccentTertiary"] = accentTertiary;
        resources["SystemFillColorAttention"] = accent;

        resources["AccentBrush"] = new SolidColorBrush(accent);
        resources["AccentSecondaryBrush"] = new SolidColorBrush(accentSecondary);
    }

    /// <summary>
    /// Reads the Windows AccentPalette (8 RGBA colors) and picks the right
    /// variant for the current theme: lighter shades for dark theme, darker
    /// shades for light theme — matching WinUI 3 behaviour.
    /// </summary>
    private static Color GetSystemAccentFromPalette(bool isDark)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\Accent");
            if (key?.GetValue("AccentPalette") is byte[] palette && palette.Length >= 32)
            {
                // Palette: [0]=Light3, [1]=Light2, [2]=Light1, [3]=Base,
                //          [4]=Dark1,  [5]=Dark2,  [6]=Dark3,  [7]=unused
                // Dark theme → Light2 (index 1), Light theme → Dark1 (index 4)
                var idx = isDark ? 1 : 4;
                var off = idx * 4;
                return Color.FromRgb(palette[off], palette[off + 1], palette[off + 2]);
            }
        }
        catch { }

        // Fallback: read the base accent from DWM
        return GetBaseAccentColor();
    }

    private static Color GetBaseAccentColor()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\DWM");
            var val = key?.GetValue("AccentColor");
            if (val is int argb)
            {
                // DWM stores as AABBGGRR (ABGR), convert to ARGB
                var a = (byte)((argb >> 24) & 0xFF);
                var b = (byte)((argb >> 16) & 0xFF);
                var g = (byte)((argb >> 8) & 0xFF);
                var r = (byte)(argb & 0xFF);
                if (a == 0) a = 0xFF; // fully opaque
                return Color.FromArgb(a, r, g, b);
            }
        }
        catch { }

        // Fallback: try SystemParameters
        try
        {
            if (SystemParameters.WindowGlassBrush is SolidColorBrush glass)
                return glass.Color;
        }
        catch { }

        // Ultimate fallback: Windows 11 default blue
        return Color.FromRgb(0x00, 0x78, 0xD4);
    }

    private static void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            if (_currentMode == AppThemeMode.System)
                ApplyResources(GetSystemTheme());
            else
                ApplySystemAccentColor(Application.Current.Resources);
        });
    }

    private static string GetSystemTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            var val = key?.GetValue("AppsUseLightTheme");
            if (val is int i) return i == 1 ? "Light" : "Dark";
        }
        catch { }
        return "Dark";
    }
}
