using LeagueScout.Domain;

namespace LeagueScout.Application.Caching;

internal static class EventQueryExtensions
{
    /// <summary>Narrows to events inside the box in SQL. Follow with an exact distance check in memory.</summary>
    public static IQueryable<PokemonEvent> WithinBox(this IQueryable<PokemonEvent> query, GeoBox box)
    {
        var minLat = box.MinLatitude;
        var maxLat = box.MaxLatitude;
        query = query.Where(e => e.Latitude >= minLat && e.Latitude <= maxLat);

        if (box.MinLongitude is { } minLng && box.MaxLongitude is { } maxLng)
        {
            query = query.Where(e => e.Longitude >= minLng && e.Longitude <= maxLng);
        }

        return query;
    }
}
