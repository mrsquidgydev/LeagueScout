using System.Globalization;
using LeagueScout.Application.Providers;
using LeagueScout.Domain;

namespace LeagueScout.Application.Caching;

/// <summary>
/// One upstream request shape. The key describes the request, not the guilds that need it,
/// so every guild whose settings map to the same key shares one cache refresh.
/// </summary>
/// <remarks>
/// Region guilds share one dataset per country (regions, event types and look-ahead are filtered locally).
/// Radius guilds share a dataset per rounded centre and radius.
/// </remarks>
public sealed record EventDataset
{
    public const PokemonGame Game = PokemonGame.TCG;

    /// <summary>Always fetched together; guilds that want only one type filter locally.</summary>
    public static readonly IReadOnlyCollection<PokemonEventType> EventTypes = [PokemonEventType.Challenge, PokemonEventType.Cup];

    /// <summary>Normalized request identity, e.g. "country:US" or "radius:32.78,-96.80,51mi".</summary>
    public required string Key { get; init; }

    public string? Country { get; init; }

    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? Radius { get; init; }
    public DistanceUnit RadiusUnit { get; init; } = DistanceUnit.Miles;

    /// <summary>Longest look-ahead any guild sharing this dataset needs. Not part of the key.</summary>
    public int LookAheadDays { get; init; } = 30;

    public bool UsesRadius => Latitude is not null && Longitude is not null && Radius is > 0;

    /// <summary>The dataset a guild reads from, or null when it has no usable location.</summary>
    public static EventDataset? ForGuild(GuildConfiguration guild)
    {
        if (guild.HasRadiusFilter)
        {
            var latitude = Normalize(Math.Round(guild.Latitude!.Value, 2));
            var longitude = Normalize(Math.Round(guild.Longitude!.Value, 2));
            // Rounding moves the centre by under 1 km; pad the radius so the dataset still covers the guild's circle.
            var radius = Math.Ceiling(guild.Radius!.Value) + 1;
            var unit = guild.RadiusUnit == DistanceUnit.Kilometers ? "km" : "mi";

            return new EventDataset
            {
                Key = string.Create(CultureInfo.InvariantCulture, $"radius:{latitude:0.00},{longitude:0.00},{radius:0}{unit}"),
                Latitude = latitude,
                Longitude = longitude,
                Radius = radius,
                RadiusUnit = guild.RadiusUnit,
                LookAheadDays = guild.LookAheadDays,
            };
        }

        if (guild.HasRegionFilter)
        {
            var country = guild.Country!.Trim().ToUpperInvariant();
            return new EventDataset
            {
                Key = $"country:{country}",
                Country = country,
                LookAheadDays = guild.LookAheadDays,
            };
        }

        return null;
    }

    /// <summary>
    /// The distinct datasets needed by guilds that can sync. Disabled or incomplete guilds need none.
    /// </summary>
    public static IReadOnlyList<EventDataset> ForGuilds(IEnumerable<GuildConfiguration> guilds) =>
        guilds
            .Where(g => g.GetSyncBlocker() is null)
            .Select(ForGuild)
            .OfType<EventDataset>()
            .GroupBy(d => d.Key, StringComparer.Ordinal)
            .Select(g => g.First() with { LookAheadDays = g.Max(d => d.LookAheadDays) })
            .OrderBy(d => d.Key, StringComparer.Ordinal)
            .ToList();

    public EventSearchCriteria ToCriteria(DateTime utcNow)
    {
        // Start a day early: the source filters on venue-local dates, which may lag UTC.
        var today = DateOnly.FromDateTime(utcNow);

        var criteria = new EventSearchCriteria
        {
            Game = Game,
            EventTypes = EventTypes,
            StartDate = today.AddDays(-1),
            EndDate = today.AddDays(LookAheadDays),
        };

        return UsesRadius
            ? criteria with { Latitude = Latitude, Longitude = Longitude, Radius = Radius, RadiusUnit = RadiusUnit }
            : criteria with { Country = Country };
    }

    /// <summary>Whether the event lies in this dataset's area. Dates and types are checked separately.</summary>
    public bool Covers(PokemonEvent pokemonEvent)
    {
        if (!UsesRadius)
        {
            return string.Equals(pokemonEvent.Country, Country, StringComparison.OrdinalIgnoreCase);
        }

        return pokemonEvent.Latitude is { } lat
               && pokemonEvent.Longitude is { } lng
               && GeoDistance.Between(Latitude!.Value, Longitude!.Value, lat, lng, RadiusUnit) <= Radius;
    }

    /// <summary>Avoids "-0.00" and "0.00" producing different keys.</summary>
    private static double Normalize(double value) => value == 0 ? 0 : value;
}
