using System.Diagnostics;
using Microsoft.Win32;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonnyTray.Models;
using SonnyTray.Services;

namespace SonnyTray.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly TailscaleClient _client;
    private bool _loading;

    private const string StartupRegistryKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string StartupValueName = "SonnyTray";

    [ObservableProperty] private bool _allowIncomingConnections;
    [ObservableProperty] private bool _useTailscaleDns;
    [ObservableProperty] private bool _useTailscaleSubnets;
    [ObservableProperty] private bool _autoUpdate;
    [ObservableProperty] private bool _runUnattended;
    [ObservableProperty] private bool _runAtStartup;

    public SettingsViewModel(TailscaleClient client)
    {
        _client = client;
    }

    public async Task LoadFromPrefsAsync(CancellationToken ct = default)
    {
        _loading = true;
        try
        {
            var prefs = await _client.GetPrefsAsync(ct);
            AllowIncomingConnections = !prefs.ShieldsUp;  // ShieldsUp is inverted
            UseTailscaleDns = prefs.CorpDNS;
            UseTailscaleSubnets = prefs.RouteAll;
            AutoUpdate = prefs.AutoUpdate?.Check ?? false;
            RunUnattended = prefs.ForceDaemon;
            RunAtStartup = IsStartupEnabled();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load prefs: {ex.Message}");
        }
        finally
        {
            _loading = false;
        }
    }

    partial void OnAllowIncomingConnectionsChanged(bool value)
    {
        if (_loading) return;
        _ = SetPrefAsync(new MaskedPrefs { ShieldsUp = !value, ShieldsUpSet = true });
    }

    partial void OnUseTailscaleDnsChanged(bool value)
    {
        if (_loading) return;
        _ = SetPrefAsync(new MaskedPrefs { CorpDNS = value, CorpDNSSet = true });
    }

    partial void OnUseTailscaleSubnetsChanged(bool value)
    {
        if (_loading) return;
        _ = SetPrefAsync(new MaskedPrefs { RouteAll = value, RouteAllSet = true });
    }

    partial void OnAutoUpdateChanged(bool value)
    {
        if (_loading) return;
        _ = SetPrefAsync(new MaskedPrefs
        {
            AutoUpdate = new AutoUpdatePrefs { Check = value, Apply = value },
            AutoUpdateSet = new AutoUpdatePrefsMask { CheckSet = true, ApplySet = true }
        });
    }

    partial void OnRunUnattendedChanged(bool value)
    {
        if (_loading) return;
        _ = SetPrefAsync(new MaskedPrefs { ForceDaemon = value, ForceDaemonSet = true });
    }

    partial void OnRunAtStartupChanged(bool value)
    {
        if (_loading) return;
        SetStartupEnabled(value);
    }

    private static bool IsStartupEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, false);
            return key?.GetValue(StartupValueName) is not null;
        }
        catch { return false; }
    }

    private static void SetStartupEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(StartupRegistryKey, true);
            if (key is null) return;
            if (enabled)
            {
                var exePath = Environment.ProcessPath ?? "";
                key.SetValue(StartupValueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(StartupValueName, false);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set startup: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        _loading = true;
        try
        {
            await _client.SetPrefsAsync(new MaskedPrefs
            {
                ShieldsUp = false, ShieldsUpSet = true,
                CorpDNS = true, CorpDNSSet = true,
                RouteAll = true, RouteAllSet = true,
                ForceDaemon = false, ForceDaemonSet = true,
                AutoUpdate = new AutoUpdatePrefs { Check = true, Apply = true },
                AutoUpdateSet = new AutoUpdatePrefsMask { CheckSet = true, ApplySet = true }
            });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to reset prefs: {ex.Message}");
        }
        finally
        {
            _loading = false;
        }

        await LoadFromPrefsAsync();
    }

    private async Task SetPrefAsync(MaskedPrefs prefs)
    {
        try
        {
            await _client.SetPrefsAsync(prefs);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to set pref: {ex.Message}");
        }
    }
}
