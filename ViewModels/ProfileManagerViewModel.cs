using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonnyTray.Models;
using SonnyTray.Services;

namespace SonnyTray.ViewModels;

public partial class ProfileManagerViewModel : ObservableObject
{
    private readonly TailscaleClient _client;
    private readonly MainViewModel _main;

    [ObservableProperty] private LoginProfile? _currentProfile;
    [ObservableProperty] private bool _showProfilePicker;
    [ObservableProperty] private bool _showAddOptions;
    [ObservableProperty] private bool _showAddCustomServer;
    [ObservableProperty] private string _newServerUrl = "";
    [ObservableProperty] private bool _isAddingServer;

    // Derived from CurrentProfile for the profile bar
    [ObservableProperty] private string _profilePicUrl = "";
    [ObservableProperty] private string _userDisplayName = "";
    [ObservableProperty] private string _controlHostName = "";
    [ObservableProperty] private bool _isCurrentHeadscale;

    public ObservableCollection<ProfileItem> Profiles { get; } = [];

    public ProfileManagerViewModel(TailscaleClient client, MainViewModel main)
    {
        _client = client;
        _main = main;
    }

    public async Task RefreshProfilesAsync(CancellationToken ct = default)
    {
        var current = await _client.GetCurrentProfileAsync(ct);
        var all = await _client.ListProfilesAsync(ct);

        CurrentProfile = current;
        ProfilePicUrl = current.UserProfile?.ProfilePicURL ?? "";
        UserDisplayName = current.UserProfile?.DisplayName ?? current.UserProfile?.LoginName ?? "";
        IsCurrentHeadscale = current.IsHeadscale;
        ControlHostName = current.ServerDisplayName;
        Profiles.Clear();
        foreach (var p in all)
        {
            var isActive = p.Id == current.Id;
            Profiles.Add(new ProfileItem
            {
                Id = p.Id,
                UserName = p.UserProfile?.DisplayName ?? p.UserProfile?.LoginName ?? p.Name,
                ServerHostName = p.ServerDisplayName,
                TailnetDisplayName = isActive && !string.IsNullOrEmpty(_main.TailnetName)
                    ? _main.TailnetName
                    : p.NetworkProfile?.DomainName ?? "",
                IsActive = isActive,
            });
        }
    }

    [RelayCommand]
    private void ToggleProfilePicker()
    {
        ShowProfilePicker = !ShowProfilePicker;
        ShowAddOptions = false;
        ShowAddCustomServer = false;
    }

    [RelayCommand]
    private async Task SwitchProfileAsync(string? profileId)
    {
        if (profileId is null || profileId == CurrentProfile?.Id) return;

        _main.SetBusyUntilLoggedIn();
        try
        {
            await TailscaleClient.EnsureDaemonRunningAsync();
            await _client.SwitchProfileAsync(profileId);

            // Kick the daemon to connect with the new profile
            await _client.StartAsync(new IpnOptions());

            await RefreshProfilesAsync();
            await _main.RefreshStatusAsync();
            await _main.Settings.LoadFromPrefsAsync();

            // Restart the IPN bus watcher so it picks up events from the new session
            _main.RestartBusWatcher();

            ShowProfilePicker = false;
            // IsBusy cleared by bus watcher when BackendState reaches Running/Stopped
        }
        catch { _main.IsBusy = false; }
    }

    [RelayCommand]
    private void ShowAddServerView()
    {
        ShowAddOptions = true;
        ShowAddCustomServer = false;
    }

    [RelayCommand]
    private async Task AddTailscaleAccountAsync()
    {
        IsAddingServer = true;
        _main.SetBusyUntilLoggedIn();
        try
        {
            await TailscaleClient.EnsureDaemonRunningAsync();
            await _client.CreateProfileAsync();
            await _client.StartAsync(new IpnOptions());
            await _client.StartLoginInteractiveAsync();
            await RefreshProfilesAsync();
            await _main.RefreshStatusAsync();
            ShowAddOptions = false;
            ShowProfilePicker = false;
        }
        finally
        {
            IsAddingServer = false;
        }
    }

    [RelayCommand]
    private void ShowCustomServerInput()
    {
        NewServerUrl = "";
        ShowAddCustomServer = true;
    }

    [RelayCommand]
    private async Task AddCustomServerAsync()
    {
        var url = NewServerUrl.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && uri.Scheme != "http"))
            return;

        IsAddingServer = true;
        _main.SetBusyUntilLoggedIn();
        try
        {
            await TailscaleClient.EnsureDaemonRunningAsync();
            await _client.CreateProfileAsync();
            await _client.StartAsync(new IpnOptions
            {
                UpdatePrefs = new TailscalePrefs { ControlURL = url }
            });
            await _client.StartLoginInteractiveAsync();
            await RefreshProfilesAsync();
            await _main.RefreshStatusAsync();
            ShowAddOptions = false;
            ShowAddCustomServer = false;
            ShowProfilePicker = false;
        }
        finally
        {
            IsAddingServer = false;
        }
    }

    [RelayCommand]
    private void CancelAddServer()
    {
        ShowAddOptions = false;
        ShowAddCustomServer = false;
    }

    [RelayCommand]
    private async Task DeleteProfileAsync(string? profileId)
    {
        if (profileId is null) return;
        await _client.DeleteProfileAsync(profileId);
        await RefreshProfilesAsync();
        await _main.RefreshStatusAsync();
    }
}

public class ProfileItem
{
    public string Id { get; set; } = "";
    public string UserName { get; set; } = "";
    public string ServerHostName { get; set; } = "";
    public string TailnetDisplayName { get; set; } = "";
    public bool IsActive { get; set; }

    public bool HasTailnetDisplayName => !string.IsNullOrEmpty(TailnetDisplayName);
}
