using System.Globalization;
using LeagueScout.Domain;
using LeagueScout.Infrastructure.TimeZones;

namespace LeagueScout.Infrastructure.PokeData;

/// <summary>Converts PokéData DTOs into normalized domain events.</summary>
internal sealed class PokeDataEventMapper(ITimeZoneResolver timeZoneResolver, string sourceUrl)
{
    public const string SourceName = "pokedata";

    private const string TournamentUrlPrefix = "https://www.pokemon.com/us/pokemon-trainer-club/play-pokemon-tournaments/";

    /// <summary>Returns the mapped event, or null with a reason when the record is unusable.</summary>
    public PokemonEvent? Map(PokeDataEventDto dto, out string? skipReason)
    {
        skipReason = null;

        var tournamentId = ParseTournamentId(dto.PokemonUrl);
        if (tournamentId is null)
        {
            skipReason = "missing pokemon_url tournament ID";
            return null;
        }

        if (ParseGame(dto.Game) is not { } game)
        {
            skipReason = $"unknown game '{dto.Game}'";
            return null;
        }

        if (!DateTime.TryParseExact(dto.When, "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var localStart))
        {
            skipReason = $"unparseable date '{dto.When}'";
            return null;
        }

        var zone = dto.Lat is { } lat && dto.Lng is { } lng ? timeZoneResolver.Resolve(lat, lng) : null;

        return new PokemonEvent
        {
            Source = SourceName,
            SourceEventId = tournamentId,
            Name = Clean(dto.Title) ?? "Pokémon Event",
            Game = game,
            EventType = ParseType(dto.Type),
            StartDateTime = ToUtc(localStart, zone),
            TimeZoneId = zone?.Id,
            VenueName = Clean(dto.ShopName),
            City = Clean(dto.City),
            Region = Clean(dto.State),
            Country = Clean(dto.CountryCode)?.ToUpperInvariant(),
            Latitude = dto.Lat,
            Longitude = dto.Lng,
            RegistrationUrl = TournamentUrlPrefix + tournamentId + "/",
            SourceUrl = sourceUrl,
        };
    }

    public static string ToQueryValue(PokemonEventType type) => type switch
    {
        PokemonEventType.Cup => "cups",
        PokemonEventType.Challenge => "challenges",
        PokemonEventType.Prerelease => "prerelease",
        PokemonEventType.Friendly => "friendly",
        _ => throw new NotSupportedException($"PokéData does not support event type {type} in this search."),
    };

    private static string? ParseTournamentId(string? pokemonUrl)
    {
        var value = Clean(pokemonUrl);
        if (value is null) return null;

        // The site accepts either a bare ID or an absolute URL; normalize to the bare ID.
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            value = uri.Segments.LastOrDefault()?.Trim('/');
        }

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static PokemonGame? ParseGame(string? game) => game?.Trim().ToLowerInvariant() switch
    {
        "tcg" => PokemonGame.TCG,
        "vg" => PokemonGame.VG,
        "go" => PokemonGame.GO,
        _ => null,
    };

    private static PokemonEventType ParseType(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        "cups" => PokemonEventType.Cup,
        "challenges" => PokemonEventType.Challenge,
        "prerelease" => PokemonEventType.Prerelease,
        "friendly" => PokemonEventType.Friendly,
        _ => PokemonEventType.Other,
    };

    private static DateTime ToUtc(DateTime local, TimeZoneInfo? zone)
    {
        if (zone is null)
        {
            // No coordinates: keep the wall-clock value as UTC rather than dropping the event.
            return DateTime.SpecifyKind(local, DateTimeKind.Utc);
        }

        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(unspecified))
        {
            // Wall-clock time skipped by a DST jump; shift past the gap.
            unspecified = unspecified.AddHours(1);
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, zone);
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
