using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SonnyTray.Models;
using SonnyTray.Services;

namespace SonnyTray.ViewModels;

public partial class ExitNodePickerViewModel : ObservableObject
{
    private readonly TailscaleClient _client;
    private readonly MainViewModel _main;

    // Static commands for use in DataTemplates
    public static RelayCommand<CountryGroup> ToggleCountryExpandedCommand { get; } =
        new(group => { if (group is not null) group.IsExpanded = !group.IsExpanded; });

    public static RelayCommand<CityGroup> ToggleCityExpandedCommand { get; } =
        new(city => { if (city is not null) city.IsExpanded = !city.IsExpanded; });

    [ObservableProperty] private string _searchQuery = "";
    [ObservableProperty] private string _selectedExitNodeId = "";
    [ObservableProperty] private ExitNodeItem? _suggestedNode;
    [ObservableProperty] private bool _isLoadingNodes;
    private bool _nodesDirty = true;

    public ObservableCollection<ExitNodeItem> TailnetExitNodes { get; } = [];
    public ObservableCollection<CountryGroup> LocationExitNodes { get; } = [];

    public ExitNodePickerViewModel(TailscaleClient client, MainViewModel main)
    {
        _client = client;
        _main = main;
    }

    partial void OnSearchQueryChanged(string value) => ApplyFilter();

    public void MarkDirty() => _nodesDirty = true;

    public async void BuildNodes(TailscaleStatus status)
    {
        if (!_nodesDirty) return;
        _nodesDirty = false;

        IsLoadingNodes = true;
        TailnetExitNodes.Clear();
        LocationExitNodes.Clear();

        // Yield to let the loading indicator render before heavy work
        await Task.Yield();

        if (status.Peer is null) { IsLoadingNodes = false; return; }

        var exitPeers = status.Peer.Values.Where(p => p.ExitNodeOption).ToList();
        var currentId = status.Peer.Values.FirstOrDefault(p => p.ExitNode)?.Id ?? "";
        SelectedExitNodeId = currentId;

        // Fetch suggested exit node from API
        try
        {
            var suggestion = await _client.SuggestExitNodeAsync();
            if (!string.IsNullOrEmpty(suggestion.Id))
            {
                SuggestedNode = new ExitNodeItem
                {
                    Id = suggestion.Id,
                    HostName = suggestion.Name,
                    City = suggestion.Location?.City ?? "",
                    Country = suggestion.Location?.Country ?? "",
                    CountryCode = suggestion.Location?.CountryCode ?? "",
                    IsOnline = true,
                    IsSelected = suggestion.Id == currentId,
                };
            }
            else
            {
                SuggestedNode = null;
            }
        }
        catch
        {
            SuggestedNode = null;
        }

        // Do the heavy grouping work off the UI thread
        var (tailnetNodes, countryGroups) = await Task.Run(() => GroupNodes(exitPeers, currentId));

        // Push results to observable collections on the UI thread
        foreach (var n in tailnetNodes)
            TailnetExitNodes.Add(n);

        // Batch-add country groups to avoid one massive layout pass
        const int batchSize = 15;
        for (int i = 0; i < countryGroups.Count; i += batchSize)
        {
            var end = Math.Min(i + batchSize, countryGroups.Count);
            for (int j = i; j < end; j++)
                LocationExitNodes.Add(countryGroups[j]);

            if (end < countryGroups.Count)
                await Task.Yield();
        }

        ApplyFilter();
        IsLoadingNodes = false;

        // Progressively reveal emoji flags after the list is rendered
        const int flagBatch = 8;
        for (int i = 0; i < LocationExitNodes.Count; i += flagBatch)
        {
            await Task.Yield();
            var end = Math.Min(i + flagBatch, LocationExitNodes.Count);
            for (int j = i; j < end; j++)
                LocationExitNodes[j].ShowFlag = true;
        }
    }

    private static (List<ExitNodeItem> tailnet, List<CountryGroup> countries) GroupNodes(
        List<PeerStatus> exitPeers, string currentId)
    {
        var tailnetNodes = new List<ExitNodeItem>();
        var nodesByCountry = new Dictionary<string, List<ExitNodeItem>>();

        foreach (var peer in exitPeers)
        {
            var item = new ExitNodeItem
            {
                Id = peer.Id,
                HostName = peer.HostName,
                DNSName = peer.DNSName,
                IsOnline = peer.Online,
                IsSelected = peer.Id == currentId,
                CountryCode = peer.Location?.CountryCode ?? "",
                Country = peer.Location?.Country ?? "",
                City = peer.Location?.City ?? "",
                CityCode = peer.Location?.CityCode ?? "",
                Priority = peer.Location?.Priority ?? 0,
            };

            if (peer.Location is not null)
            {
                var key = peer.Location.CountryCode;
                if (!nodesByCountry.TryGetValue(key, out var list))
                {
                    list = [];
                    nodesByCountry[key] = list;
                }
                list.Add(item);
            }
            else
            {
                tailnetNodes.Add(item);
            }
        }

        // Sort tailnet nodes
        var sortedTailnet = tailnetNodes.OrderByDescending(n => n.IsOnline).ThenBy(n => n.HostName).ToList();

        // Build country groups for location-based exit nodes
        var countryGroups = new List<CountryGroup>();
        foreach (var (cc, nodes) in nodesByCountry.OrderBy(kv => kv.Value[0].Country))
        {
            var cityGroups = nodes
                .GroupBy(n => n.CityCode)
                .OrderBy(g => g.First().City)
                .Select(g =>
                {
                    var relays = g.OrderByDescending(n => n.Priority).ToList();
                    var best = relays.FirstOrDefault(n => n.IsOnline) ?? relays[0];
                    var cityGroup = new CityGroup
                    {
                        CityName = g.First().City,
                        CityCode = g.Key,
                        BestId = best.Id,
                        IsOnline = relays.Any(n => n.IsOnline),
                        IsSelected = relays.Any(n => n.IsSelected),
                        RelayCount = relays.Count,
                    };
                    foreach (var r in relays)
                        cityGroup.Relays.Add(r);
                    return cityGroup;
                })
                .ToList();

            var group = new CountryGroup
            {
                CountryCode = cc,
                Country = nodes[0].Country,
                Flag = CountryCodeToFlag(cc),
                CityCount = cityGroups.Count,
                IsSelected = cityGroups.Any(c => c.IsSelected),
            };

            foreach (var city in cityGroups)
                group.Cities.Add(city);

            var best = nodes.Where(n => n.IsOnline).OrderByDescending(n => n.Priority).FirstOrDefault();
            if (best is not null)
            {
                group.BestAvailable = new ExitNodeItem
                {
                    Id = best.Id,
                    HostName = $"Best in {nodes[0].Country}",
                    City = "Best Available",
                    IsOnline = true,
                    CountryCode = cc,
                    Country = nodes[0].Country,
                };
            }

            countryGroups.Add(group);
        }

        return (sortedTailnet, countryGroups);
    }

    private void ApplyFilter()
    {
        var q = SearchQuery;
        var hasQuery = !string.IsNullOrWhiteSpace(q);

        // Tailnet nodes
        foreach (var node in TailnetExitNodes)
            node.IsVisible = !hasQuery || node.MatchesSearch(q);

        // Location-based nodes: country > city hierarchy
        foreach (var country in LocationExitNodes)
        {
            var countryMatches = !hasQuery
                || country.Country.Contains(q, StringComparison.OrdinalIgnoreCase)
                || country.CountryCode.Contains(q, StringComparison.OrdinalIgnoreCase);

            var anyChildVisible = false;
            foreach (var city in country.Cities)
            {
                var cityVisible = countryMatches
                    || city.CityName.Contains(q, StringComparison.OrdinalIgnoreCase)
                    || city.CityCode.Contains(q, StringComparison.OrdinalIgnoreCase);
                city.IsVisible = cityVisible;
                if (cityVisible) anyChildVisible = true;
            }

            country.IsVisible = countryMatches || anyChildVisible;
        }
    }

    [RelayCommand]
    private async Task SelectExitNodeAsync(string? nodeId)
    {
        nodeId ??= "";
        await _client.SetPrefsAsync(new MaskedPrefs
        {
            ExitNodeID = nodeId,
            ExitNodeIDSet = true,
        });
        SelectedExitNodeId = nodeId;
        _main.ShowExitNodePicker = false;
    }

    [RelayCommand]
    private async Task ClearExitNodeAsync()
    {
        await SelectExitNodeAsync("");
    }

    internal static string CountryCodeToFlag(string countryCode)
    {
        if (countryCode.Length != 2) return "🌐";
        // Convert 2-letter country code to Unicode regional indicator flag sequence
        var upper = countryCode.ToUpperInvariant();
        return string.Concat(
            char.ConvertFromUtf32(0x1F1E6 + upper[0] - 'A'),
            char.ConvertFromUtf32(0x1F1E6 + upper[1] - 'A'));
    }
}

public partial class ExitNodeItem : ObservableObject
{
    public string Id { get; set; } = "";
    public string HostName { get; set; } = "";
    public string DNSName { get; set; } = "";
    public bool IsOnline { get; set; }
    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isVisible = true;
    public string CountryCode { get; set; } = "";
    public string Country { get; set; } = "";
    public string City { get; set; } = "";
    public string CityCode { get; set; } = "";
    public int Priority { get; set; }

    public string Label => !string.IsNullOrEmpty(City)
        ? $"{City}"
        : HostName;

    public bool MatchesSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return Country.Contains(query, StringComparison.OrdinalIgnoreCase)
            || City.Contains(query, StringComparison.OrdinalIgnoreCase)
            || HostName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || CountryCode.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}

public partial class CountryGroup : ObservableObject
{
    public string CountryCode { get; set; } = "";
    public string Country { get; set; } = "";
    public string Flag { get; set; } = "🌐";
    public int CityCount { get; set; }
    public bool IsSelected { get; set; }
    public ExitNodeItem? BestAvailable { get; set; }
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isVisible = true;
    [ObservableProperty] private bool _showFlag;
    public ObservableCollection<CityGroup> Cities { get; } = [];

    public bool MatchesSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return Country.Contains(query, StringComparison.OrdinalIgnoreCase)
            || CountryCode.Contains(query, StringComparison.OrdinalIgnoreCase)
            || Cities.Any(c => c.MatchesSearch(query));
    }
}

public partial class CityGroup : ObservableObject
{
    public string CityName { get; set; } = "";
    public string CityCode { get; set; } = "";
    public string BestId { get; set; } = "";
    public bool IsOnline { get; set; }
    public bool IsSelected { get; set; }
    public int RelayCount { get; set; }
    public string RelayCountLabel => RelayCount > 1 ? $"· {RelayCount} relays" : "";
    [ObservableProperty] private bool _isExpanded;
    [ObservableProperty] private bool _isVisible = true;
    public ObservableCollection<ExitNodeItem> Relays { get; } = [];

    public bool MatchesSearch(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return true;
        return CityName.Contains(query, StringComparison.OrdinalIgnoreCase)
            || Relays.Any(r => r.MatchesSearch(query));
    }
}
