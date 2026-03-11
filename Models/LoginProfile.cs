using System.Text.Json;
using System.Text.Json.Serialization;

namespace SonnyTray.Models;

public sealed class LoginProfile
{
    [JsonPropertyName("ID")]
    public string Id { get; set; } = "";

    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("ControlURL")]
    public string ControlURL { get; set; } = "";

    [JsonPropertyName("NodeID")]
    public string NodeID { get; set; } = "";

    [JsonPropertyName("UserProfile")]
    public UserProfile? UserProfile { get; set; }

    [JsonPropertyName("NetworkProfile")]
    public NetworkProfile? NetworkProfile { get; set; }

    public bool IsHeadscale =>
        !string.IsNullOrEmpty(ControlURL)
        && !ControlURL.Contains("controlplane.tailscale.com", StringComparison.OrdinalIgnoreCase)
        && !ControlURL.Contains("login.tailscale.com", StringComparison.OrdinalIgnoreCase);

    public string ServerDisplayName =>
        IsHeadscale && Uri.TryCreate(ControlURL, UriKind.Absolute, out var uri)
            ? uri.Host
            : "Tailscale";
}

public sealed class NetworkProfile
{
    [JsonPropertyName("MagicDNSName")]
    public string MagicDNSName { get; set; } = "";

    [JsonPropertyName("DomainName")]
    public string DomainName { get; set; } = "";
}

public sealed class IpnOptions
{
    [JsonPropertyName("UpdatePrefs")]
    public TailscalePrefs? UpdatePrefs { get; set; }
}

public sealed class IpnNotify
{
    [JsonPropertyName("State")]
    public int? State { get; set; }

    [JsonPropertyName("Prefs")]
    public TailscalePrefs? Prefs { get; set; }

    [JsonPropertyName("NetMap")]
    public JsonElement? NetMap { get; set; }

    [JsonPropertyName("Version")]
    public string? Version { get; set; }

    [JsonPropertyName("BrowseToURL")]
    public string? BrowseToURL { get; set; }
}

public sealed class ExitNodeSuggestion
{
    [JsonPropertyName("ID")]
    public string Id { get; set; } = "";

    [JsonPropertyName("Name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("Location")]
    public Location? Location { get; set; }
}
