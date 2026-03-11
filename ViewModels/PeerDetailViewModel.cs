using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonnyTray.Models;
using SonnyTray.Services;

namespace SonnyTray.ViewModels;

public partial class PeerDetailViewModel : ObservableObject
{
    private readonly TailscaleClient _client;
    private CancellationTokenSource? _pingCts;
    private CancellationTokenSource? _refreshCts;

    [ObservableProperty] private PeerItem? _peer;
    [ObservableProperty] private bool _isPinging;
    [ObservableProperty] private string _pingStatus = "";
    [ObservableProperty] private double _lastLatencyMs;
    [ObservableProperty] private double _avgLatencyMs;
    [ObservableProperty] private double _minLatencyMs;
    [ObservableProperty] private double _maxLatencyMs;
    [ObservableProperty] private int _pingCount;
    [ObservableProperty] private int _pingErrors;
    [ObservableProperty] private string _lastConnectionType = "";
    [ObservableProperty] private string _lastEndpoint = "";

    public ObservableCollection<PingDataPoint> PingHistory { get; } = [];

    public PeerDetailViewModel(TailscaleClient client)
    {
        _client = client;
    }

    public void LoadPeer(PeerItem peer)
    {
        StopPing();
        Peer = peer;
        PingHistory.Clear();
        PingStatus = "";
        LastLatencyMs = 0;
        AvgLatencyMs = 0;
        MinLatencyMs = 0;
        MaxLatencyMs = 0;
        PingCount = 0;
        PingErrors = 0;
        LastConnectionType = peer.ConnectionType;
        LastEndpoint = peer.CurAddr;
        StartRefreshLoop();
    }

    public void UpdatePeer(PeerItem peer)
    {
        Peer = peer;
        if (!IsPinging)
        {
            LastConnectionType = peer.ConnectionType;
            LastEndpoint = peer.CurAddr;
        }
    }

    public void StopRefreshLoop()
    {
        _refreshCts?.Cancel();
        _refreshCts = null;
    }

    private void StartRefreshLoop()
    {
        StopRefreshLoop();
        _refreshCts = new CancellationTokenSource();
        _ = RefreshLoopAsync(_refreshCts.Token);
    }

    private async Task RefreshLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(5000, ct);
                if (Peer is null) break;

                try
                {
                    var status = await _client.GetStatusAsync(ct);
                    if (status.Peer is null) continue;

                    foreach (var (_, ps) in status.Peer)
                    {
                        if (ps.Id != Peer.Id) continue;

                        Peer = new PeerItem
                        {
                            Id = ps.Id,
                            HostName = ps.HostName,
                            DNSName = ps.DNSName,
                            OS = ps.OS,
                            IP = ps.TailscaleIPs?.FirstOrDefault() ?? "",
                            IPv6 = ps.TailscaleIPs is { Count: > 1 } ? ps.TailscaleIPs[1] : "",
                            IsOnline = ps.Online,
                            IsExitNode = ps.ExitNode,
                            ExitNodeOption = ps.ExitNodeOption,
                            Active = ps.Active,
                            Relay = ps.Relay,
                            Tags = ps.Tags,
                            RxBytes = ps.RxBytes,
                            TxBytes = ps.TxBytes,
                            PublicKey = ps.PublicKey,
                            CurAddr = ps.CurAddr,
                            LastSeen = ps.LastSeen,
                            Created = ps.Created,
                            UserID = ps.UserID,
                        };

                        if (!IsPinging)
                        {
                            LastConnectionType = Peer.ConnectionType;
                            LastEndpoint = Peer.CurAddr;
                        }
                        break;
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch { /* tolerate transient errors */ }
            }
        }
        catch (OperationCanceledException) { }
    }

    [RelayCommand]
    private void TogglePing()
    {
        if (IsPinging)
        {
            _pingCts?.Cancel();
            return;
        }
        _ = RunPingLoopAsync();
    }

    private async Task RunPingLoopAsync()
    {
        if (Peer is null || string.IsNullOrEmpty(Peer.IP)) return;

        IsPinging = true;
        PingStatus = "Pinging...";
        PingHistory.Clear();
        PingCount = 0;
        PingErrors = 0;
        MinLatencyMs = double.MaxValue;
        MaxLatencyMs = 0;

        _pingCts = new CancellationTokenSource();
        var ct = _pingCts.Token;

        try
        {
            while (!ct.IsCancellationRequested && PingCount < 100)
            {
                try
                {
                    var result = await _client.PingPeerAsync(Peer.IP, ct);
                    PingCount++;

                    if (!string.IsNullOrEmpty(result.Err))
                    {
                        PingErrors++;
                        PingHistory.Add(new PingDataPoint
                        {
                            Index = PingCount,
                            LatencyMs = -1,
                            IsError = true,
                            IsDirect = false,
                        });
                        PingStatus = $"Error: {result.Err}";
                    }
                    else
                    {
                        var ms = result.LatencyMs;
                        LastLatencyMs = ms;
                        if (ms < MinLatencyMs) MinLatencyMs = ms;
                        if (ms > MaxLatencyMs) MaxLatencyMs = ms;

                        var validPoints = PingHistory.Where(p => !p.IsError).ToList();
                        AvgLatencyMs = validPoints.Count > 0
                            ? (validPoints.Sum(p => p.LatencyMs) + ms) / (validPoints.Count + 1)
                            : ms;

                        LastConnectionType = result.IsDirect ? "Direct" : $"Relay ({result.DERPRegionCode})";
                        LastEndpoint = result.Endpoint;

                        PingHistory.Add(new PingDataPoint
                        {
                            Index = PingCount,
                            LatencyMs = ms,
                            IsError = false,
                            IsDirect = result.IsDirect,
                        });

                        PingStatus = $"{ms:F1}ms via {LastConnectionType}";
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    PingErrors++;
                    PingCount++;
                    PingHistory.Add(new PingDataPoint
                    {
                        Index = PingCount,
                        LatencyMs = -1,
                        IsError = true,
                        IsDirect = false,
                    });
                    PingStatus = $"Error: {ex.Message}";
                }

                try { await Task.Delay(1000, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            IsPinging = false;            _pingCts?.Dispose();
            _pingCts = null;            if (PingCount > 0)
            {
                if (MinLatencyMs == double.MaxValue) MinLatencyMs = 0;
                PingStatus = $"Done: {PingCount} sent, {PingErrors} errors, avg {AvgLatencyMs:F1}ms";
            }
        }
    }

    [RelayCommand]
    private void StopPing()
    {
        _pingCts?.Cancel();
    }

    [RelayCommand]
    private void CopyField(string? value)
    {
        if (!string.IsNullOrEmpty(value))
            Clipboard.SetText(value);
    }
}

public class PingDataPoint
{
    public int Index { get; set; }
    public double LatencyMs { get; set; }
    public bool IsError { get; set; }
    public bool IsDirect { get; set; }
}
