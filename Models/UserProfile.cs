using System.Text.Json.Serialization;

namespace SonnyTray.Models;

public sealed class UserProfile
{
    [JsonPropertyName("ID")]
    public long Id { get; set; }

    [JsonPropertyName("LoginName")]
    public string LoginName { get; set; } = "";

    [JsonPropertyName("DisplayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("ProfilePicURL")]
    public string ProfilePicURL { get; set; } = "";
}
