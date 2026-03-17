using System.Text.Json;
using System.Text.Json.Serialization;

namespace SonnyTray.Models;

public sealed class PeerStatus
{
    [JsonPropertyName("ID")]
    public string Id { get; set; } = "";

    [JsonPropertyName("PublicKey")]
    public string PublicKey { get; set; } = "";

    [JsonPropertyName("HostName")]
    public string HostName { get; set; } = "";

    [JsonPropertyName("DNSName")]
    public string DNSName { get; set; } = "";

    [JsonPropertyName("OS")]
    public string OS { get; set; } = "";

    [JsonPropertyName("TailscaleIPs")]
    public List<string>? TailscaleIPs { get; set; }

    [JsonPropertyName("Tags")]
    public List<string>? Tags { get; set; }

    [JsonPropertyName("Relay")]
    public string Relay { get; set; } = "";

    [JsonPropertyName("Online")]
    public bool Online { get; set; }

    [JsonPropertyName("ExitNode")]
    public bool ExitNode { get; set; }

    [JsonPropertyName("ExitNodeOption")]
    public bool ExitNodeOption { get; set; }

    [JsonPropertyName("Active")]
    public bool Active { get; set; }

    [JsonPropertyName("UserID")]
    public long UserID { get; set; }

    [JsonPropertyName("Location")]
    public Location? Location { get; set; }

    [JsonPropertyName("RxBytes")]
    public long RxBytes { get; set; }

    [JsonPropertyName("TxBytes")]
    public long TxBytes { get; set; }

    [JsonPropertyName("CurAddr")]
    public string CurAddr { get; set; } = "";

    [JsonPropertyName("LastSeen")]
    public DateTime? LastSeen { get; set; }

    [JsonPropertyName("Created")]
    public DateTime? Created { get; set; }

    [JsonPropertyName("InNetworkMap")]
    public bool InNetworkMap { get; set; }

    [JsonPropertyName("InMagicSock")]
    public bool InMagicSock { get; set; }

    [JsonPropertyName("InEngine")]
    public bool InEngine { get; set; }

    [JsonPropertyName("CapMap")]
    public Dictionary<string, JsonElement?>? CapMap { get; set; }
}
