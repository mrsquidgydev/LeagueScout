using System.Text.Json.Serialization;

namespace LeagueScout.Infrastructure.PokeData;

/// <summary>
/// One element of the JSON array returned by <c>https://pokedata.ovh/events2/events.php</c>.
/// See docs/pokedata-investigation.md. Never leaves the PokéData integration.
/// </summary>
internal sealed class PokeDataEventDto
{
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("title")] public string? Title { get; set; }

    /// <summary>Venue-local wall-clock time, "yyyy-MM-dd HH:mm:ss", no offset.</summary>
    [JsonPropertyName("when")] public string? When { get; set; }

    [JsonPropertyName("shopName")] public string? ShopName { get; set; }
    [JsonPropertyName("city")] public string? City { get; set; }
    [JsonPropertyName("state")] public string? State { get; set; }
    [JsonPropertyName("country_code")] public string? CountryCode { get; set; }
    [JsonPropertyName("lat")] public double? Lat { get; set; }
    [JsonPropertyName("lng")] public double? Lng { get; set; }

    /// <summary>Official Play! Pokémon tournament ID, e.g. "26-10-014168".</summary>
    [JsonPropertyName("pokemon_url")] public string? PokemonUrl { get; set; }

    [JsonPropertyName("league")] public string? League { get; set; }

    /// <summary>"tcg", "vg" or "go".</summary>
    [JsonPropertyName("game")] public string? Game { get; set; }

    /// <summary>"cups", "challenges", "prerelease" or "friendly".</summary>
    [JsonPropertyName("type")] public string? Type { get; set; }
}
