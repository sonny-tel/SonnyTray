using System.Windows;
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

        if (mode == AppThemeMode.System && !_listening)
        {
            _listening = true;
            SystemEvents.UserPreferenceChanged += OnSystemPreferenceChanged;
        }
        else if (mode != AppThemeMode.System && _listening)
        {
            SystemEvents.UserPreferenceChanged -= OnSystemPreferenceChanged;
            _listening = false;
        }
    }

    public static void ApplyTheme(string theme)
    {
        _currentMode = theme == "Dark" ? AppThemeMode.Dark : AppThemeMode.Light;
        ApplyResources(theme);
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

        ThemeChanged?.Invoke();
    }

    private static void OnSystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        if (_currentMode != AppThemeMode.System) return;
        Application.Current.Dispatcher.BeginInvoke(() =>
        {
            ApplyResources(GetSystemTheme());
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
