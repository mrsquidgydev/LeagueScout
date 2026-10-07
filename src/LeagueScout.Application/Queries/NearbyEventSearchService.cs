using Microsoft.Extensions.Caching.Memory;
using LeagueScout.Application.Providers;
using LeagueScout.Domain;

namespace LeagueScout.Application.Queries;

public sealed record NearbyEvent(PokemonEvent Event, double? Distance);

public sealed record NearbyEventSearchResult(GeocodedLocation Location, IReadOnlyList<NearbyEvent> Events);

/// <summary>
/// Live lookup of events around a user-supplied place. Nothing is stored in the database.
/// Geocoding and provider results are cached in memory to limit load on both services.
/// </summary>
public class NearbyEventSearchService(IGeocoder geocoder, IEventProvider provider, IMemoryCache cache, TimeProvider clock)
{
    private static readonly IReadOnlyCollection<PokemonEventType> EventTypes = [PokemonEventType.Challenge, PokemonEventType.Cup];

    private static readonly TimeSpan GeocodeCacheDuration = TimeSpan.FromDays(7);
    private static readonly TimeSpan GeocodeMissCacheDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan EventCacheDuration = TimeSpan.FromMinutes(15);

    /// <summary>Upcoming events within the radius, soonest first. Returns null when the place is not found.</summary>
    public async Task<NearbyEventSearchResult?> SearchAsync(
        string place, double radius, DistanceUnit unit, int lookAheadDays, CancellationToken cancellationToken = default)
    {
        var location = await GeocodeAsync(place.Trim(), cancellationToken);
        if (location is null) return null;

        var now = clock.GetUtcNow().UtcDateTime;
        var today = DateOnly.FromDateTime(now);

        // Round the centre (~1 km) so searches for the same area share cache entries.
        // Start a day early: the source filters on venue-local dates, which may lag UTC.
        var criteria = new EventSearchCriteria
        {
            Game = PokemonGame.TCG,
            EventTypes = EventTypes,
            Latitude = Math.Round(location.Latitude, 2),
            Longitude = Math.Round(location.Longitude, 2),
            Radius = radius,
            RadiusUnit = unit,
            StartDate = today.AddDays(-1),
            EndDate = today.AddDays(lookAheadDays),
        };

        var events = await GetEventsAsync(criteria, cancellationToken);

        var nearby = events
            .Where(e => e.Status == EventStatus.Active && !e.HasStarted(now))
            .Select(e => new NearbyEvent(e, Distance(location, e, unit)))
            .OrderBy(e => e.Event.StartDateTime)
            .ThenBy(e => e.Distance)
            .ToList();

        return new NearbyEventSearchResult(location, nearby);
    }

    private async Task<GeocodedLocation?> GeocodeAsync(string place, CancellationToken cancellationToken)
    {
        var key = new GeocodeKey(place.ToLowerInvariant());
        if (cache.TryGetValue(key, out GeocodedLocation? cached)) return cached;

        var location = await geocoder.GeocodeAsync(place, cancellationToken);
        cache.Set(key, location, location is null ? GeocodeMissCacheDuration : GeocodeCacheDuration);
        return location;
    }

    private async Task<IReadOnlyCollection<PokemonEvent>> GetEventsAsync(EventSearchCriteria criteria, CancellationToken cancellationToken)
    {
        var key = new EventsKey(criteria.Latitude!.Value, criteria.Longitude!.Value, criteria.Radius!.Value,
            criteria.RadiusUnit, criteria.StartDate, criteria.EndDate);
        if (cache.TryGetValue(key, out IReadOnlyCollection<PokemonEvent>? cached) && cached is not null) return cached;

        var events = await provider.GetEventsAsync(criteria, cancellationToken);
        cache.Set(key, events, EventCacheDuration);
        return events;
    }

    /// <summary>Great-circle distance from the searched place, or null when the event has no coordinates.</summary>
    public static double? Distance(GeocodedLocation from, PokemonEvent to, DistanceUnit unit)
    {
        if (to.Latitude is not { } lat || to.Longitude is not { } lng) return null;

        var earthRadius = unit == DistanceUnit.Kilometers ? 6371.0 : 3958.8;
        var dLat = ToRadians(lat - from.Latitude);
        var dLng = ToRadians(lng - from.Longitude);
        var a = Math.Pow(Math.Sin(dLat / 2), 2)
                + Math.Cos(ToRadians(from.Latitude)) * Math.Cos(ToRadians(lat)) * Math.Pow(Math.Sin(dLng / 2), 2);
        return earthRadius * 2 * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;

    private readonly record struct GeocodeKey(string Query);

    private readonly record struct EventsKey(
        double Latitude, double Longitude, double Radius, DistanceUnit Unit, DateOnly StartDate, DateOnly EndDate);
}
