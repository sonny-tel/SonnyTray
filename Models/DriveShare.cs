using System.Text.Json.Serialization;

namespace SonnyTray.Models;

public sealed class DriveShare
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("as")]
    public string As { get; set; } = "";
}
