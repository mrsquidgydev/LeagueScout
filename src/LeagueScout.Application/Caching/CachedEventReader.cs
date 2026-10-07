using Microsoft.EntityFrameworkCore;
using LeagueScout.Application.Persistence;
using LeagueScout.Domain;

namespace LeagueScout.Application.Caching;

/// <summary>Matches a guild's filters against the shared event cache. Never calls the event source.</summary>
public class CachedEventReader(IApplicationDbContext db)
{
    /// <summary>Active, not yet started events inside the guild's location, type and look-ahead filters, soonest first. Untracked.</summary>
    public async Task<IReadOnlyList<PokemonEvent>> GetMatchingAsync(
        GuildConfiguration guild, DateTime utcNow, CancellationToken cancellationToken = default)
    {
        var types = new List<PokemonEventType>();
        if (guild.IncludeChallenges) types.Add(PokemonEventType.Challenge);
        if (guild.IncludeCups) types.Add(PokemonEventType.Cup);

        // Include the whole last look-ahead day: source dates are venue-local.
        var windowEnd = DateOnly.FromDateTime(utcNow).AddDays(guild.LookAheadDays + 1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

        var query = db.Events
            .AsNoTracking()
            .Where(e => e.Status == EventStatus.Active
                        && e.Game == EventDataset.Game
                        && e.StartDateTime > utcNow
                        && e.StartDateTime < windowEnd
                        && types.Contains(e.EventType));

        IEnumerable<PokemonEvent> matches;
        if (guild.HasRadiusFilter)
        {
            var (lat, lng, radius, unit) = (guild.Latitude!.Value, guild.Longitude!.Value, guild.Radius!.Value, guild.RadiusUnit);
            var nearby = await query.WithinBox(GeoDistance.BoundingBox(lat, lng, radius, unit)).ToListAsync(cancellationToken);
            matches = nearby.Where(e => e.Latitude is { } eventLat
                                        && e.Longitude is { } eventLng
                                        && GeoDistance.Between(lat, lng, eventLat, eventLng, unit) <= radius);
        }
        else if (guild.HasRegionFilter)
        {
            var country = guild.Country!.Trim().ToUpperInvariant();
            var inCountry = await query.Where(e => e.Country == country).ToListAsync(cancellationToken);

            // Regions are matched here rather than in SQL so the comparison ignores case on every provider.
            var regions = guild.Regions
                .Select(r => r.Trim())
                .Where(r => r.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            matches = regions.Count == 0
                ? inCountry
                : inCountry.Where(e => e.Region is not null && regions.Contains(e.Region));
        }
        else
        {
            return [];
        }

        return matches.OrderBy(e => e.StartDateTime).ToList();
    }
}
