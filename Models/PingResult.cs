using System.Text.Json.Serialization;

namespace SonnyTray.Models;

public sealed class PingResult
{
    [JsonPropertyName("LatencySeconds")]
    public double LatencySeconds { get; set; }

    [JsonPropertyName("NodeIP")]
    public string NodeIP { get; set; } = "";

    [JsonPropertyName("NodeName")]
    public string NodeName { get; set; } = "";

    [JsonPropertyName("Endpoint")]
    public string Endpoint { get; set; } = "";

    [JsonPropertyName("DERPRegionID")]
    public int DERPRegionID { get; set; }

    [JsonPropertyName("DERPRegionCode")]
    public string DERPRegionCode { get; set; } = "";

    [JsonPropertyName("PeerAPIPort")]
    public int PeerAPIPort { get; set; }

    [JsonPropertyName("IsLocalIP")]
    public bool IsLocalIP { get; set; }

    [JsonPropertyName("Err")]
    public string Err { get; set; } = "";

    public double LatencyMs => LatencySeconds * 1000;
    public bool IsDirect => DERPRegionID == 0 && string.IsNullOrEmpty(DERPRegionCode);
    public bool IsRelay => DERPRegionID > 0;
}
