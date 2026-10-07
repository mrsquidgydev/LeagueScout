using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using LeagueScout.Application.Persistence;
using LeagueScout.Application.Providers;
using LeagueScout.Domain;

namespace LeagueScout.Application.Caching;

public enum CacheHealth
{
    /// <summary>No successful refresh yet.</summary>
    NoData,

    Healthy,

    /// <summary>The last request failed; cached data is still within the maximum age.</summary>
    Retrying,

    /// <summary>Cached data is older than the maximum age. It is still served.</summary>
    Stale,
}

public sealed record DatasetDiagnostics(
    string DatasetKey,
    CacheHealth Health,
    DateTime? LastAttemptAt,
    DateTime? LastSuccessAt,
    TimeSpan? CacheAge,
    DateTime? NextAllowedRequestAt,
    int ConsecutiveFailures,
    int? LastFailureStatusCode,
    int LastResultCount);

/// <param name="Dataset">The dataset this guild reads from, or null when its location is not set.</param>
/// <param name="MatchingUpcomingEventCount">Upcoming cached events matching this guild's filters, or null when its location is not set.</param>
public sealed record EventCacheDiagnostics(
    DatasetDiagnostics? Dataset,
    int CachedEventCount,
    int UpcomingActiveEventCount,
    int? MatchingUpcomingEventCount,
    DateTime? LastGuildSyncAt,
    EventCacheCounters Counters);

/// <summary>Read-only cache health for operators. Contains no secrets or exception details.</summary>
public class EventCacheDiagnosticsService(
    IApplicationDbContext db,
    CachedEventReader cachedEvents,
    IEventProvider provider,
    EventCacheStatistics statistics,
    IOptions<EventCacheOptions> options,
    TimeProvider clock)
{
    public async Task<EventCacheDiagnostics> GetAsync(ulong guildId, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var guild = await db.GuildConfigurations
            .AsNoTracking()
            .SingleOrDefaultAsync(g => g.GuildId == guildId, cancellationToken);
        var dataset = guild is null ? null : EventDataset.ForGuild(guild);

        DatasetDiagnostics? datasetDiagnostics = null;
        if (dataset is not null)
        {
            var source = provider.SourceName;
            var state = await db.DatasetSyncStates
                .AsNoTracking()
                .SingleOrDefaultAsync(s => s.Source == source && s.DatasetKey == dataset.Key, cancellationToken);

            datasetDiagnostics = new DatasetDiagnostics(
                dataset.Key,
                GetHealth(state, now, options.Value.MaxCacheAge),
                state?.LastAttemptAt,
                state?.LastSuccessAt,
                state?.CacheAge(now),
                state?.NextAllowedRequestAt,
                state?.ConsecutiveFailures ?? 0,
                state?.LastFailureStatusCode,
                state?.LastResultCount ?? 0);
        }

        var cachedCount = await db.Events.CountAsync(cancellationToken);
        var upcomingCount = await db.Events.CountAsync(
            e => e.Status == EventStatus.Active && e.StartDateTime > now, cancellationToken);

        int? matchingCount = guild is not null && dataset is not null
            ? (await cachedEvents.GetMatchingAsync(guild, now, cancellationToken)).Count
            : null;

        return new EventCacheDiagnostics(
            datasetDiagnostics, cachedCount, upcomingCount, matchingCount, guild?.LastSyncedAt, statistics.Snapshot());
    }

    public static CacheHealth GetHealth(DatasetSyncState? state, DateTime utcNow, TimeSpan maxCacheAge)
    {
        if (state?.LastSuccessAt is null) return CacheHealth.NoData;
        if (state.CacheAge(utcNow) > maxCacheAge) return CacheHealth.Stale;
        return state.ConsecutiveFailures > 0 ? CacheHealth.Retrying : CacheHealth.Healthy;
    }
}
