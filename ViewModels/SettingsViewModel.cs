using System.Collections.ObjectModel;
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

    // TailDrive
    [ObservableProperty] private bool _showAddShare;
    [ObservableProperty] private string _newShareName = "";
    [ObservableProperty] private string _newSharePath = "";
    [ObservableProperty] private bool _driveError;
    public ObservableCollection<DriveShareItem> DriveShares { get; } = [];

    // TailDrive mounting
    private const string TailDriveEndpoint = "http://100.100.100.100:8080/";
    private const string WebClientParamsKey = @"SYSTEM\CurrentControlSet\Services\WebClient\Parameters";
    private const string FileSizeLimitValue = "FileSizeLimitInBytes";
    private const uint MaxFileSize = 4294967295; // 4 GB
    [ObservableProperty] private bool _showMountDrive;
    [ObservableProperty] private bool _isTailDriveMounted;
    [ObservableProperty] private string _tailDriveLetter = "";
    [ObservableProperty] private string _mountDriveLetter = "";
    [ObservableProperty] private bool _isFileSizePatched;
    public List<string> AvailableDriveLetters => GetAvailableDriveLetters();

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
            await RefreshDriveSharesAsync();
            await RefreshMountedDrivesAsync();
            RefreshFileSizeLimit();
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

    // ---------- TailDrive ----------

    public async Task RefreshDriveSharesAsync()
    {
        try
        {
            var shares = await _client.GetDriveSharesAsync();
            DriveError = false;
            DriveShares.Clear();
            foreach (var s in shares)
                DriveShares.Add(new DriveShareItem { Name = s.Name, Path = s.Path });
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load drive shares: {ex.Message}");
            DriveError = true;
            DriveShares.Clear();
        }
    }

    [RelayCommand]
    private void ShowAddShareView()
    {
        NewShareName = "";
        NewSharePath = "";
        ShowAddShare = true;
    }

    [RelayCommand]
    private void BrowseShareFolder()
    {
        var dialog = new OpenFolderDialog { Title = "Select folder to share" };
        var window = System.Windows.Application.Current.Windows.OfType<System.Windows.Window>()
            .FirstOrDefault(w => w.IsActive) ?? System.Windows.Application.Current.MainWindow;
        App.SuppressDeactivate = true;
        try
        {
            if (dialog.ShowDialog(window) == true)
                NewSharePath = dialog.FolderName;
        }
        finally { App.SuppressDeactivate = false; }
    }

    [RelayCommand]
    private async Task AddShareAsync()
    {
        var name = NewShareName.Trim();
        var path = NewSharePath.Trim();
        if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) return;

        try
        {
            await _client.AddDriveShareAsync(new DriveShare { Name = name, Path = path });
            ShowAddShare = false;
            await RefreshDriveSharesAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to add share: {ex.Message}");
        }
    }

    [RelayCommand]
    private void CancelAddShare()
    {
        ShowAddShare = false;
    }

    [RelayCommand]
    private async Task RemoveShareAsync(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        try
        {
            await _client.RemoveDriveShareAsync(name);
            await RefreshDriveSharesAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to remove share: {ex.Message}");
        }
    }

    [RelayCommand]
    private static void OpenShareFolder(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch { }
    }

    // ---------- TailDrive mounting ----------

    public async Task RefreshMountedDrivesAsync()
    {
        try
        {
            var psi = new ProcessStartInfo("net", "use")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var proc = Process.Start(psi);
            if (proc is null) return;
            var output = await proc.StandardOutput.ReadToEndAsync();
            await proc.WaitForExitAsync();

            IsTailDriveMounted = false;
            TailDriveLetter = "";

            foreach (var line in output.Split('\n'))
            {
                var trimmed = line.Trim();
                if (!trimmed.Contains("100.100.100.100", StringComparison.Ordinal)) continue;

                var parts = trimmed.Split([' '], StringSplitOptions.RemoveEmptyEntries);
                // Drive letter could be at index 0 (no status) or 1 (with status like "OK")
                foreach (var part in parts)
                {
                    var candidate = part.TrimEnd(':');
                    if (candidate.Length == 1 && char.IsLetter(candidate[0]))
                    {
                        IsTailDriveMounted = true;
                        TailDriveLetter = candidate;
                        break;
                    }
                }
                if (IsTailDriveMounted) break;
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to check TailDrive mount: {ex.Message}");
        }
        OnPropertyChanged(nameof(AvailableDriveLetters));
    }

    private void RefreshFileSizeLimit()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(WebClientParamsKey, false);
            var val = key?.GetValue(FileSizeLimitValue);
            if (val is int intVal)
                IsFileSizePatched = (uint)intVal >= MaxFileSize;
            else if (val is long longVal)
                IsFileSizePatched = (uint)longVal >= MaxFileSize;
            else
                IsFileSizePatched = false;
        }
        catch
        {
            IsFileSizePatched = false;
        }
    }

    [RelayCommand]
    private void PatchFileSizeLimit()
    {
        try
        {
            // Requires UAC elevation — launch reg.exe as admin
            var psi = new ProcessStartInfo("reg",
                $@"add ""HKLM\{WebClientParamsKey}"" /v {FileSizeLimitValue} /t REG_DWORD /d {MaxFileSize} /f")
            {
                UseShellExecute = true,
                Verb = "runas",
                CreateNoWindow = true,
            };
            var proc = Process.Start(psi);
            proc?.WaitForExit();
            RefreshFileSizeLimit();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to patch WebDAV file size limit: {ex.Message}");
        }
    }

    [RelayCommand]
    private void ShowMountDriveView()
    {
        MountDriveLetter = "";
        ShowMountDrive = true;
        OnPropertyChanged(nameof(AvailableDriveLetters));
    }

    [RelayCommand]
    private void CancelMountDrive()
    {
        ShowMountDrive = false;
    }

    [RelayCommand]
    private async Task MountDriveAsync()
    {
        var letter = MountDriveLetter.Trim().TrimEnd(':');
        if (string.IsNullOrEmpty(letter)) return;

        try
        {
            var psi = new ProcessStartInfo("net", $"use {letter}: {TailDriveEndpoint}")
            {
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var proc = Process.Start(psi);
            if (proc is null) return;
            await proc.WaitForExitAsync();

            // Set a friendly label so Explorer shows "Taildrive (F:)" instead of "DavWWWRoot"
            if (proc.ExitCode == 0)
            {
                try
                {
                    var keyPath = $@"Software\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons\{letter}\DefaultLabel";
                    using var key = Registry.CurrentUser.CreateSubKey(keyPath);
                    key.SetValue("", "Taildrive");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to set drive label: {ex.Message}");
                }
            }

            ShowMountDrive = false;
            await RefreshMountedDrivesAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to mount TailDrive: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task UnmountTailDriveAsync()
    {
        if (!IsTailDriveMounted || string.IsNullOrEmpty(TailDriveLetter)) return;
        try
        {
            var letter = TailDriveLetter;
            var psi = new ProcessStartInfo("net", $"use {letter}: /delete /y")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var proc = Process.Start(psi);
            if (proc is null) return;
            await proc.WaitForExitAsync();

            // Clean up the drive label registry key
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree(
                    $@"Software\Microsoft\Windows\CurrentVersion\Explorer\DriveIcons\{letter}", false);
            }
            catch { }

            await RefreshMountedDrivesAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to unmount TailDrive: {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenTailDrive()
    {
        if (!IsTailDriveMounted || string.IsNullOrEmpty(TailDriveLetter)) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = $"{TailDriveLetter}:\\",
                UseShellExecute = true,
            });
        }
        catch { }
    }

    private static List<string> GetAvailableDriveLetters()
    {
        var used = System.IO.DriveInfo.GetDrives().Select(d => d.Name[0]).ToHashSet();
        return Enumerable.Range('D', 23) // D through Z
            .Select(c => ((char)c).ToString())
            .Where(c => !used.Contains(c[0]))
            .ToList();
    }
}

public class DriveShareItem
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}
