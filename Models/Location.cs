using System.Text.Json.Serialization;

namespace SonnyTray.Models;

public sealed class Location
{
    [JsonPropertyName("Country")]
    public string Country { get; set; } = "";

    [JsonPropertyName("CountryCode")]
    public string CountryCode { get; set; } = "";

    [JsonPropertyName("City")]
    public string City { get; set; } = "";

    [JsonPropertyName("CityCode")]
    public string CityCode { get; set; } = "";

    [JsonPropertyName("Latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("Longitude")]
    public double Longitude { get; set; }

    [JsonPropertyName("Priority")]
    public int Priority { get; set; }
}
