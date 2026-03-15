using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonnyTray.Models;
using SonnyTray.Services;

namespace SonnyTray.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly TailscaleClient _client;
    private CancellationTokenSource? _busCts;
    private HashSet<string> _wireGuardOnlyIds = [];

    private enum BusyWait { None, UntilSettled, UntilLoggedOut }
    private BusyWait _busyWait;

    public ExitNodePickerViewModel ExitNodePicker { get; }
    public ProfileManagerViewModel ProfileManager { get; }
    public SettingsViewModel Settings { get; }

    [ObservableProperty] private string _backendState = "Unknown";
    [ObservableProperty] private string _selfHostName = "";
    [ObservableProperty] private string _selfIP = "";
    [ObservableProperty] private string _selfIPv6 = "";
    [ObservableProperty] private string _selfDNSName = "";
    [ObservableProperty] private string _tailnetName = "";
    [ObservableProperty] private string _currentExitNodeName = "None";
    [ObservableProperty] private string _currentExitNodeId = "";
    [ObservableProperty] private string _currentExitNodeCountryCode = "";

    public string CurrentExitNodeFlag =>
        string.IsNullOrEmpty(CurrentExitNodeCountryCode) ? "" : ExitNodePickerViewModel.CountryCodeToFlag(CurrentExitNodeCountryCode);
    public bool HasExitNodeFlag => !string.IsNullOrEmpty(CurrentExitNodeCountryCode);

    [ObservableProperty] private bool _isConnected;
    [ObservableProperty] private bool _isLoading = true;
    [ObservableProperty] private string _version = "";
    [ObservableProperty] private bool _showExitNodePicker;
    [ObservableProperty] private bool _showSettings;
    [ObservableProperty] private bool _showPeerDetail;
    private TailscaleStatus? _latestStatus;
    [ObservableProperty] private bool _showCopyMenu;
    [ObservableProperty] private bool _needsLogin;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNotBusy))]
    private bool _isBusy;

    public bool IsNotBusy => !IsBusy;

    // User role capabilities
    public ObservableCollection<string> UserRoles { get; } = [];
    [ObservableProperty] private bool _hasUserRoles;

    /// <summary>
    /// Marks IsBusy and keeps it set until the IPN bus reports a fully settled
    /// post-login state (Running or Stopped).
    /// </summary>
    public void SetBusyUntilLoggedIn()
    {
        _busyWait = BusyWait.UntilSettled;
        IsBusy = true;
    }

    /// <summary>
    /// Marks IsBusy until logout completes (NoState/Stopped).
    /// </summary>
    private void SetBusyUntilLoggedOut()
    {
        _busyWait = BusyWait.UntilLoggedOut;
        IsBusy = true;
    }

    [ObservableProperty] private string _loginUserName = "";

    public ObservableCollection<PeerItem> Peers { get; } = [];
    public PeerDetailViewModel PeerDetail { get; private set; } = null!;

    public MainViewModel(TailscaleClient client)
    {
        _client = client;
        ExitNodePicker = new ExitNodePickerViewModel(client, this);
        ProfileManager = new ProfileManagerViewModel(client, this);
        Settings = new SettingsViewModel(client);
        PeerDetail = new PeerDetailViewModel(client);
    }

    public async Task InitializeAsync()
    {
        try
        {
            await TailscaleClient.EnsureDaemonRunningAsync();
            await ProfileManager.RefreshProfilesAsync();
            await RefreshStatusAsync();
            await Settings.LoadFromPrefsAsync();
            _ = WatchBusAsync();
        }
        catch (Exception ex)
        {
            BackendState = "Error";
            Debug.WriteLine($"Init failed: {ex}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    public async Task RefreshStatusAsync()
    {
        var status = await _client.GetStatusAsync();
        ApplyStatus(status);
    }

    private void ApplyStatus(TailscaleStatus status)
    {
        Version = status.Version;
        BackendState = status.BackendState;
        IsConnected = status.BackendState == "Running";
        NeedsLogin = status.BackendState == "NeedsLogin";

        // Prefer the display name from Self.CapMap, fall back to CurrentTailnet.Name
        TailnetName = status.Self?.CapMap is { } cap
            && cap.TryGetValue("tailnet-display-name", out var names)
            && names is [var displayName, ..]
                ? displayName
                : status.CurrentTailnet?.Name ?? "";

        if (status.Self is { } self)
        {
            SelfHostName = self.HostName;
            SelfIP = self.TailscaleIPs?.FirstOrDefault() ?? "";
            SelfIPv6 = self.TailscaleIPs?.Skip(1).FirstOrDefault() ?? "";
            SelfDNSName = self.DNSName.TrimEnd('.');

            // Use the live User map from status for profile info (profiles/current can be stale)
            if (status.User is not null
                && status.User.TryGetValue(self.UserID.ToString(), out var selfUser))
            {
                if (!string.IsNullOrEmpty(selfUser.ProfilePicURL))
                    ProfileManager.ProfilePicUrl = selfUser.ProfilePicURL;
                if (!string.IsNullOrEmpty(selfUser.DisplayName))
                    ProfileManager.UserDisplayName = selfUser.DisplayName;
                else if (!string.IsNullOrEmpty(selfUser.LoginName))
                    ProfileManager.UserDisplayName = selfUser.LoginName;
                LoginUserName = selfUser.LoginName;
            }
            else
            {
                LoginUserName = ProfileManager.CurrentProfile?.UserProfile?.LoginName ?? "";
            }
        }
        else
        {
            // Determine login user from profile
            LoginUserName = ProfileManager.CurrentProfile?.UserProfile?.LoginName ?? "";
        }

        // Build peer list hide exit node infrastructure (peers with Location data)
        Peers.Clear();
        if (status.Peer is not null)
        {
            foreach (var (_, peer) in status.Peer)
            {
                if (peer.Location is not null)
                    continue;

                Peers.Add(new PeerItem
                {
                    Id = peer.Id,
                    HostName = peer.HostName,
                    DNSName = peer.DNSName,
                    OS = peer.OS,
                    IP = peer.TailscaleIPs?.FirstOrDefault() ?? "",
                    IPv6 = peer.TailscaleIPs is { Count: > 1 } ? peer.TailscaleIPs[1] : "",
                    IsOnline = peer.Online,
                    IsExitNode = peer.ExitNode,
                    ExitNodeOption = peer.ExitNodeOption,
                    Active = peer.Active,
                    Relay = peer.Relay,
                    Tags = peer.Tags,
                    RxBytes = peer.RxBytes,
                    TxBytes = peer.TxBytes,
                    PublicKey = peer.PublicKey,
                    CurAddr = peer.CurAddr,
                    LastSeen = peer.LastSeen,
                    Created = peer.Created,
                    UserID = peer.UserID,
                });
            }
        }

        // Current exit node
        CurrentExitNodeId = "";
        CurrentExitNodeName = "None";
        CurrentExitNodeCountryCode = "";
        OnPropertyChanged(nameof(CurrentExitNodeFlag));
        OnPropertyChanged(nameof(HasExitNodeFlag));
        if (status.Peer is not null)
        {
            foreach (var (_, peer) in status.Peer)
            {
                if (peer.ExitNode)
                {
                    CurrentExitNodeId = peer.Id;
                    CurrentExitNodeCountryCode = peer.Location?.CountryCode ?? "";
                    OnPropertyChanged(nameof(CurrentExitNodeFlag));
                    OnPropertyChanged(nameof(HasExitNodeFlag));
                    CurrentExitNodeName = peer.Location is not null
                        ? $"{peer.Location.City}, {peer.Location.Country}"
                        : peer.HostName;
                    break;
                }
            }
        }

        // Determine TailDrive capabilities from Self CapMap
        var capMap = status.Self?.CapMap;
        Settings.CanDriveShare = capMap?.ContainsKey("drive:share") == true;
        Settings.CanDriveAccess = capMap?.ContainsKey("drive:access") == true;

        // Extract user role capabilities
        ApplyRolesFromCapMap(capMap);

        _latestStatus = status;
        ExitNodePicker.MarkDirty();

        // Update peer detail if open
        if (ShowPeerDetail && PeerDetail.Peer is { } currentPeer && status.Peer is not null)
        {
            var freshPeer = Peers.FirstOrDefault(p => p.Id == currentPeer.Id);
            if (freshPeer is not null)
                PeerDetail.UpdatePeer(freshPeer);
        }

        // If picker is already open, rebuild immediately
        if (ShowExitNodePicker)
            ExitNodePicker.BuildNodes(status);
    }

    partial void OnShowExitNodePickerChanged(bool value)
    {
        if (value && _latestStatus is not null)
            ExitNodePicker.BuildNodes(_latestStatus);
    }

    partial void OnShowSettingsChanged(bool value)
    {
    }

    partial void OnShowPeerDetailChanged(bool value)
    {
        if (!value)
            PeerDetail.StopRefreshLoop();
    }

    /// <summary>
    /// Restarts the IPN bus watcher. Call after switching profiles to pick up events
    /// from the new session.
    /// </summary>
    public void RestartBusWatcher()
    {
        _busCts?.Cancel();
        _ = WatchBusAsync();
    }

    private async Task WatchBusAsync()
    {
        _busCts?.Cancel();
        _busCts = new CancellationTokenSource();
        var ct = _busCts.Token;

        try
        {
            await foreach (var notify in _client.WatchIPNBusAsync(ct))
            {
                // Handle login flow tailscaled sends a URL to open in the browser
                if (!string.IsNullOrEmpty(notify.BrowseToURL))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = notify.BrowseToURL,
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to open login URL: {ex.Message}");
                    }
                }

                if (notify.NetMap is { } netMap)
                    UpdateWireGuardOnlyIds(netMap);

                if (notify.State is not null || notify.Prefs is not null || notify.NetMap is not null)
                {
                    try
                    {
                        await ProfileManager.RefreshProfilesAsync();
                        await RefreshStatusAsync();
                    }
                    catch { /* tolerate transient errors during refresh */ }

                    try { await Settings.RefreshDriveStateAsync(); }
                    catch { /* tolerate transient errors */ }

                    // Only clear IsBusy once we've reached a settled state.
                    if (_busyWait == BusyWait.UntilSettled)
                    {
                        // Login / profile switch: wait for Running or Stopped
                        if (BackendState is "Running" or "Stopped")
                        {
                            _busyWait = BusyWait.None;
                            IsBusy = false;
                        }
                    }
                    else if (_busyWait == BusyWait.UntilLoggedOut)
                    {
                        // Logout: wait for NoState or Stopped
                        if (BackendState is "NoState" or "Stopped")
                        {
                            _busyWait = BusyWait.None;
                            IsBusy = false;
                        }
                    }
                    else
                    {
                        IsBusy = false;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Debug.WriteLine($"IPN bus error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task ToggleConnectionAsync()
    {
        IsBusy = true;
        try
        {
            var running = BackendState == "Running";
            await _client.SetPrefsAsync(new MaskedPrefs
            {
                WantRunning = !running,
                WantRunningSet = true
            });
            // IsBusy cleared by bus watcher when state changes; safety timeout as fallback
            _ = ClearBusyAfterTimeout();
        }
        catch { IsBusy = false; }
    }

    private async Task ClearBusyAfterTimeout()
    {
        await Task.Delay(10_000);
        IsBusy = false;
    }

    [RelayCommand]
    private async Task LoginAsync()
    {
        SetBusyUntilLoggedIn();
        try { await _client.StartLoginInteractiveAsync(); }
        catch { _busyWait = BusyWait.None; IsBusy = false; }
    }

    [RelayCommand]
    private async Task LogoutAsync()
    {
        SetBusyUntilLoggedOut();
        try
        {
            await _client.LogoutAsync();
            await ProfileManager.RefreshProfilesAsync();
            await RefreshStatusAsync();
        }
        catch { _busyWait = BusyWait.None; IsBusy = false; }
    }

    [RelayCommand]
    private void ToggleExitNodePicker()
    {
        ShowExitNodePicker = !ShowExitNodePicker;
    }

    [RelayCommand]
    private void OpenPeerDetail(PeerItem peer)
    {
        ShowExitNodePicker = false;
        ShowSettings = false;
        ShowCopyMenu = false;
        PeerDetail.LoadPeer(peer);
        ShowPeerDetail = true;
    }

    [RelayCommand]
    private async Task ToggleSettingsAsync()
    {
        ShowSettings = !ShowSettings;
        if (ShowSettings)
            await Settings.LoadFromPrefsAsync();
    }

    [RelayCommand]
    private void CopyIP()
    {
        if (!string.IsNullOrEmpty(SelfIP))
            Clipboard.SetText(SelfIP);
    }

    [RelayCommand]
    private void ToggleCopyMenu()
    {
        ShowCopyMenu = !ShowCopyMenu;
    }

    [RelayCommand]
    private void CopyToClipboard(string? text)
    {
        if (!string.IsNullOrEmpty(text))
            Clipboard.SetText(text);
        ShowCopyMenu = false;
    }

    [RelayCommand]
    private void OpenAdminConsole()
    {
        var url = ProfileManager.CurrentProfile?.IsHeadscale == true
            ? ProfileManager.CurrentProfile.ControlURL
            : "https://login.tailscale.com/admin";
        if (!string.IsNullOrEmpty(url))
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
    }

    private void UpdateWireGuardOnlyIds(JsonElement netMap)
    {
        var ids = new HashSet<string>();
        if (netMap.TryGetProperty("Peers", out var peers) && peers.ValueKind == JsonValueKind.Array)
        {
            foreach (var node in peers.EnumerateArray())
            {
                if (node.TryGetProperty("IsWireGuardOnly", out var wgOnly) && wgOnly.GetBoolean()
                    && node.TryGetProperty("StableID", out var stableId))
                {
                    ids.Add(stableId.GetString() ?? "");
                }
            }
        }
        _wireGuardOnlyIds = ids;
    }

    // Roles ordered from highest to lowest priority.
    // A higher role suppresses all roles it implies (listed after it).
    private static readonly (string Segment, string Display, string[] Suppresses)[] RolePriority =
    [
        ("is-owner",         "Owner",         ["is-admin", "is-network-admin"]),
        ("is-admin",         "Admin",         ["is-network-admin"]),
        ("is-network-admin", "Network Admin", []),
        ("is-billing-admin", "Billing Admin", []),
        ("is-it-admin",      "IT Admin",      []),
        ("is-auditor",       "Auditor",       []),
        ("is-member",        "Member",        []),
    ];

    private void ApplyRolesFromCapMap(Dictionary<string, List<string>>? capMap)
    {
        UserRoles.Clear();
        if (capMap is null)
        {
            HasUserRoles = false;
            return;
        }

        // Collect all is-* segments present
        var presentSegments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in capMap.Keys)
        {
            var seg = key.Split('/')[^1];
            if (seg.StartsWith("is-", StringComparison.OrdinalIgnoreCase))
                presentSegments.Add(seg);
        }

        // Walk priority list; skip roles suppressed by a higher one already added
        var suppressed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (segment, display, suppresses) in RolePriority)
        {
            if (suppressed.Contains(segment)) continue;
            if (!presentSegments.Remove(segment)) continue;
            UserRoles.Add(display);
            foreach (var s in suppresses) suppressed.Add(s);
        }

        // Any unknown is-* roles not in the priority table
        foreach (var seg in presentSegments)
        {
            if (suppressed.Contains(seg)) continue;
            UserRoles.Add(FormatRoleSegment(seg));
        }

        HasUserRoles = UserRoles.Count > 0;
    }

    private static string FormatRoleSegment(string segment)
    {
        var rolePart = segment.StartsWith("is-", StringComparison.OrdinalIgnoreCase) ? segment[3..] : segment;
        return string.Join(' ', rolePart.Split('-')
            .Select(w => w.Length > 0 ? char.ToUpperInvariant(w[0]) + w[1..] : w));
    }

    public void Dispose()
    {
        _busCts?.Cancel();
        _busCts?.Dispose();
    }
}

public class PeerItem
{
    public string Id { get; set; } = "";
    public string HostName { get; set; } = "";
    public string DNSName { get; set; } = "";
    public string OS { get; set; } = "";
    public string IP { get; set; } = "";
    public string IPv6 { get; set; } = "";
    public bool IsOnline { get; set; }
    public bool IsExitNode { get; set; }
    public bool ExitNodeOption { get; set; }
    public bool Active { get; set; }
    public string Relay { get; set; } = "";
    public List<string>? Tags { get; set; }
    public long RxBytes { get; set; }
    public long TxBytes { get; set; }
    public string PublicKey { get; set; } = "";
    public string CurAddr { get; set; } = "";
    public DateTime? LastSeen { get; set; }
    public DateTime? Created { get; set; }
    public long UserID { get; set; }

    public bool IsDirect => !string.IsNullOrEmpty(CurAddr);
    public bool HasRelay => !string.IsNullOrEmpty(Relay);
    public bool HasIPv6 => !string.IsNullOrEmpty(IPv6);
    public bool HasTags => Tags is { Count: > 0 };
    public string ConnectionType => IsDirect ? "Direct" : HasRelay ? $"DERP Relay ({Relay})" : "None";
    public string TagsDisplay => HasTags ? string.Join(", ", Tags!) : "None";

    public string RxDisplay => FormatBytes(RxBytes);
    public string TxDisplay => FormatBytes(TxBytes);

    /// <summary>
    /// Friendly display name: if HostName is useless (e.g. "localhost"), derive from DNSName.
    /// Normalizes names like "Google Pixel 9" to "google-pixel-9".
    /// </summary>
    public string DisplayName
    {
        get
        {
            var raw = HostName;
            if (string.IsNullOrEmpty(raw)
                || raw.Equals("localhost", StringComparison.OrdinalIgnoreCase))
            {
                // Take the first label from the FQDN: "google-pixel-9.tailnet.ts.net." → "google-pixel-9"
                raw = DNSName.Split('.').FirstOrDefault() ?? HostName;
            }

            if (string.IsNullOrEmpty(raw)) return HostName;

            // Normalize: lowercase, replace spaces with hyphens
            return raw.Trim().Replace(' ', '-').ToLowerInvariant();
        }
    }

    public string OSIcon => OS.ToLowerInvariant() switch
    {
        "windows" => "\uE770",    // Segoe MDL2 Computer icon
        "linux" => "\uE7EF",
        "macos" or "macOS" => "\uE8FC",
        "android" => "\uE8EA",
        "ios" or "iOS" => "\uE8EA",
        _ => "\uE703"
    };

    internal static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
        _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
    };
}
