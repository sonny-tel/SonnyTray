using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Principal;
using System.ServiceProcess;
using System.Text.Json;
using SonnyTray.Models;

namespace SonnyTray.Services;

public sealed class TailscaleClient : IDisposable
{
    private const string PipeName = @"ProtectedPrefix\Administrators\Tailscale\tailscaled";
    private const string BaseUrl = "http://local-tailscaled.sock";

    private readonly HttpClient _http;

    public TailscaleClient()
    {
        var handler = new SocketsHttpHandler
        {
            ConnectCallback = async (ctx, ct) =>
            {
                // TokenImpersonation lets the pipe server identify our Windows user needed for auth.
                var pipe = new NamedPipeClientStream(
                    ".", PipeName, PipeDirection.InOut,
                    PipeOptions.Asynchronous, TokenImpersonationLevel.Impersonation);

                const int maxRetries = 20;
                for (int i = 0; ; i++)
                {
                    try
                    {
                        await pipe.ConnectAsync(1000, ct);
                        break;
                    }
                    catch (System.TimeoutException) when (i < maxRetries - 1)
                    {
                        await Task.Delay(250, ct);
                    }
                }
                return pipe;
            }
        };

        _http = new HttpClient(handler)
        {
            BaseAddress = new Uri(BaseUrl)
        };
        // Required by tailscaled to identify this as a legitimate local API client.
        _http.DefaultRequestHeaders.Add("Tailscale-Cap", "133");
    }

    // ---------- Status ----------

    public async Task<TailscaleStatus> GetStatusAsync(CancellationToken ct = default)
    {
        return await GetJsonAsync<TailscaleStatus>("/localapi/v0/status", ct);
    }

    public async Task<TailscaleStatus> GetStatusWithoutPeersAsync(CancellationToken ct = default)
    {
        return await GetJsonAsync<TailscaleStatus>("/localapi/v0/status?peers=false", ct);
    }

    // ---------- Preferences ----------

    public async Task<TailscalePrefs> GetPrefsAsync(CancellationToken ct = default)
    {
        return await GetJsonAsync<TailscalePrefs>("/localapi/v0/prefs", ct);
    }

    public async Task<TailscalePrefs> SetPrefsAsync(MaskedPrefs prefs, CancellationToken ct = default)
    {
        var response = await _http.PatchAsJsonAsync("/localapi/v0/prefs", prefs, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<TailscalePrefs>(ct)
            ?? throw new InvalidOperationException("Null prefs response");
    }

    // ---------- Exit Node ----------

    public async Task<ExitNodeSuggestion> SuggestExitNodeAsync(CancellationToken ct = default)
    {
        return await GetJsonAsync<ExitNodeSuggestion>("/localapi/v0/suggest-exit-node", ct);
    }

    public async Task SetUseExitNodeAsync(bool enabled, CancellationToken ct = default)
    {
        var url = $"/localapi/v0/set-use-exit-node-enabled?enabled={enabled.ToString().ToLowerInvariant()}";
        var response = await _http.PostAsync(url, null, ct);
        response.EnsureSuccessStatusCode();
    }

    // ---------- Connection ----------

    public async Task StartLoginInteractiveAsync(CancellationToken ct = default)
    {
        var response = await _http.PostAsync("/localapi/v0/login-interactive", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task StartAsync(IpnOptions options, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/localapi/v0/start", options, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        var response = await _http.PostAsync("/localapi/v0/logout", null, ct);
        response.EnsureSuccessStatusCode();
    }

    // ---------- Profiles ----------

    public async Task<LoginProfile[]> ListProfilesAsync(CancellationToken ct = default)
    {
        return await GetJsonAsync<LoginProfile[]>("/localapi/v0/profiles/", ct);
    }

    public async Task<LoginProfile> GetCurrentProfileAsync(CancellationToken ct = default)
    {
        return await GetJsonAsync<LoginProfile>("/localapi/v0/profiles/current", ct);
    }

    public async Task SwitchProfileAsync(string profileId, CancellationToken ct = default)
    {
        var response = await _http.PostAsync(
            $"/localapi/v0/profiles/{Uri.EscapeDataString(profileId)}", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task CreateProfileAsync(CancellationToken ct = default)
    {
        var response = await _http.PutAsync("/localapi/v0/profiles/", null, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task DeleteProfileAsync(string profileId, CancellationToken ct = default)
    {
        var response = await _http.DeleteAsync(
            $"/localapi/v0/profiles/{Uri.EscapeDataString(profileId)}", ct);
        response.EnsureSuccessStatusCode();
    }

    // ---------- IPN Bus (streaming) ----------

    public async IAsyncEnumerable<IpnNotify> WatchIPNBusAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            "/localapi/v0/watch-ipn-bus?mask=0");
        request.Headers.Add("Connection", "keep-alive");

        using var response = await _http.SendAsync(request,
            HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            var notify = JsonSerializer.Deserialize<IpnNotify>(line);
            if (notify is not null)
                yield return notify;
        }
    }

    // ---------- Ping ----------

    public async Task<PingResult> PingPeerAsync(string ipOrHost, CancellationToken ct = default)
    {
        var url = $"/localapi/v0/ping?ip={Uri.EscapeDataString(ipOrHost)}&type=disco";
        var response = await _http.PostAsync(url, null, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<PingResult>(ct)
            ?? throw new InvalidOperationException("Null ping response");
    }

    // ---------- TailDrive ----------

    public async Task<DriveShare[]> GetDriveSharesAsync(CancellationToken ct = default)
    {
        return await _http.GetFromJsonAsync<DriveShare[]>("/localapi/v0/drive/shares", ct) ?? [];
    }

    public async Task AddDriveShareAsync(DriveShare share, CancellationToken ct = default)
    {
        var response = await _http.PutAsJsonAsync("/localapi/v0/drive/shares", share, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RemoveDriveShareAsync(string name, CancellationToken ct = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Delete, "/localapi/v0/drive/shares")
        {
            Content = new StringContent(name)
        };
        var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
    }

    public async Task RenameDriveShareAsync(string oldName, string newName, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("/localapi/v0/drive/shares",
            new[] { oldName, newName }, ct);
        response.EnsureSuccessStatusCode();
    }

    // ---------- Helpers ----------

    private async Task<T> GetJsonAsync<T>(string path, CancellationToken ct)
    {
        return await _http.GetFromJsonAsync<T>(path, ct)
            ?? throw new InvalidOperationException($"Null response from {path}");
    }

    // ---------- Service Management ----------

    private const string ServiceName = "Tailscale";

    /// <summary>
    /// Ensures the tailscaled Windows service is running. Starts it if stopped.
    /// Returns true if the service is now running, false if it couldn't be started.
    /// </summary>
    public static async Task<bool> EnsureDaemonRunningAsync(CancellationToken ct = default)
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            if (sc.Status == ServiceControllerStatus.Running)
                return true;

            if (sc.Status == ServiceControllerStatus.Stopped ||
                sc.Status == ServiceControllerStatus.Paused)
            {
                sc.Start();
                await Task.Run(() => sc.WaitForStatus(
                    ServiceControllerStatus.Running, TimeSpan.FromSeconds(30)), ct);
                // Give the named pipe a moment to become available
                await Task.Delay(500, ct);
            }

            sc.Refresh();
            return sc.Status == ServiceControllerStatus.Running;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to ensure tailscaled is running: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Checks if the tailscaled Windows service is installed and running.
    /// </summary>
    public static bool IsDaemonRunning()
    {
        try
        {
            using var sc = new ServiceController(ServiceName);
            return sc.Status == ServiceControllerStatus.Running;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose() => _http.Dispose();
}
