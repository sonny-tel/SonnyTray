using System.Text.Json.Serialization;

namespace SonnyTray.Models;

public sealed class TailscaleStatus
{
    [JsonPropertyName("Version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("BackendState")]
    public string BackendState { get; set; } = "";

    [JsonPropertyName("Self")]
    public PeerStatus? Self { get; set; }

    [JsonPropertyName("Peer")]
    public Dictionary<string, PeerStatus>? Peer { get; set; }

    [JsonPropertyName("User")]
    public Dictionary<string, UserProfile>? User { get; set; }

    [JsonPropertyName("ExitNodeStatus")]
    public ExitNodeStatus? ExitNodeStatus { get; set; }

    [JsonPropertyName("CurrentTailnet")]
    public TailnetStatus? CurrentTailnet { get; set; }
}

public sealed class ExitNodeStatus
{
    [JsonPropertyName("ID")]
    public string Id { get; set; } = "";

    [JsonPropertyName("Online")]
    public bool Online { get; set; }

    [JsonPropertyName("TailscaleIPs")]
    public List<string>? TailscaleIPs { get; set; }
}

public sealed class TailnetStatus
{
    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("MagicDNSSuffix")]
    public string MagicDNSSuffix { get; set; } = "";

    [JsonPropertyName("MagicDNSEnabled")]
    public bool MagicDNSEnabled { get; set; }
}
