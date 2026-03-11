using System.Text.Json.Serialization;

namespace SonnyTray.Models;

public sealed class TailscalePrefs
{
    [JsonPropertyName("ControlURL")]
    public string ControlURL { get; set; } = "";

    [JsonPropertyName("ExitNodeID")]
    public string ExitNodeID { get; set; } = "";

    [JsonPropertyName("ExitNodeAllowLANAccess")]
    public bool ExitNodeAllowLANAccess { get; set; }

    [JsonPropertyName("WantRunning")]
    public bool WantRunning { get; set; }

    [JsonPropertyName("Hostname")]
    public string Hostname { get; set; } = "";

    [JsonPropertyName("ShieldsUp")]
    public bool ShieldsUp { get; set; }

    [JsonPropertyName("CorpDNS")]
    public bool CorpDNS { get; set; }

    [JsonPropertyName("RouteAll")]
    public bool RouteAll { get; set; }

    [JsonPropertyName("ForceDaemon")]
    public bool ForceDaemon { get; set; }

    [JsonPropertyName("AutoUpdate")]
    public AutoUpdatePrefs? AutoUpdate { get; set; }

    [JsonPropertyName("AdvertiseRoutes")]
    public List<string>? AdvertiseRoutes { get; set; }
}

public sealed class AutoUpdatePrefs
{
    [JsonPropertyName("Check")]
    public bool Check { get; set; }

    [JsonPropertyName("Apply")]
    public bool? Apply { get; set; }
}

public sealed class MaskedPrefs
{
    [JsonPropertyName("ExitNodeID")]
    public string? ExitNodeID { get; set; }

    [JsonPropertyName("ExitNodeIDSet")]
    public bool ExitNodeIDSet { get; set; }

    [JsonPropertyName("ExitNodeAllowLANAccess")]
    public bool? ExitNodeAllowLANAccess { get; set; }

    [JsonPropertyName("ExitNodeAllowLANAccessSet")]
    public bool ExitNodeAllowLANAccessSet { get; set; }

    [JsonPropertyName("WantRunning")]
    public bool? WantRunning { get; set; }

    [JsonPropertyName("WantRunningSet")]
    public bool WantRunningSet { get; set; }

    [JsonPropertyName("ShieldsUp")]
    public bool? ShieldsUp { get; set; }

    [JsonPropertyName("ShieldsUpSet")]
    public bool ShieldsUpSet { get; set; }

    [JsonPropertyName("CorpDNS")]
    public bool? CorpDNS { get; set; }

    [JsonPropertyName("CorpDNSSet")]
    public bool CorpDNSSet { get; set; }

    [JsonPropertyName("RouteAll")]
    public bool? RouteAll { get; set; }

    [JsonPropertyName("RouteAllSet")]
    public bool RouteAllSet { get; set; }

    [JsonPropertyName("ForceDaemon")]
    public bool? ForceDaemon { get; set; }

    [JsonPropertyName("ForceDaemonSet")]
    public bool ForceDaemonSet { get; set; }

    [JsonPropertyName("AdvertiseRoutes")]
    public List<string>? AdvertiseRoutes { get; set; }

    [JsonPropertyName("AdvertiseRoutesSet")]
    public bool AdvertiseRoutesSet { get; set; }

    [JsonPropertyName("AutoUpdate")]
    public AutoUpdatePrefs? AutoUpdate { get; set; }

    [JsonPropertyName("AutoUpdateSet")]
    public AutoUpdatePrefsMask? AutoUpdateSet { get; set; }
}

public sealed class AutoUpdatePrefsMask
{
    [JsonPropertyName("CheckSet")]
    public bool CheckSet { get; set; }

    [JsonPropertyName("ApplySet")]
    public bool ApplySet { get; set; }
}
