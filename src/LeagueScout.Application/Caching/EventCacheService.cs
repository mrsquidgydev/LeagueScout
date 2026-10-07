using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeagueScout.Application.Persistence;
using LeagueScout.Application.Providers;
using LeagueScout.Domain;

namespace LeagueScout.Application.Caching;

public enum CacheRefreshOutcome
{
    /// <summary>The cache is fresh; no request was made.</summary>
    Fresh,

    /// <summary>A recent failure's backoff has not elapsed; no request was made.</summary>
    BackingOff,

    Refreshed,

    /// <summary>The request failed. Cached events were left untouched.</summary>
    Failed,
}

public sealed record CacheRefreshResult(string DatasetKey, CacheRefreshOutcome Outcome)
{
    public int EventsReceived { get; set; }
    public int EventsAdded { get; set; }
    public int EventsUpdated { get; set; }

    /// <summary>Upcoming events this refresh should have listed but did not, including those marked removed.</summary>
    public int EventsMissing { get; set; }

    /// <summary>Events that reached the missing threshold on this refresh.</summary>
    public int EventsRemoved { get; set; }

    /// <summary>True when a Discord post may need creating or editing.</summary>
    public bool HasChanges => EventsAdded + EventsUpdated + EventsRemoved > 0;
}

/// <summary>
/// Keeps the shared event cache in step with the event source. Requests are made per dataset, never per guild,
/// at most once per refresh interval, with backoff after failures. Failed requests leave cached events untouched.
/// Never talks to Discord.
/// </summary>
public class EventCacheService(
    IApplicationDbContext db,
    IEventProvider provider,
    IKeyedLock locks,
    EventCacheStatistics statistics,
    IOptions<EventCacheOptions> options,
    TimeProvider clock,
    ILogger<EventCacheService> logger)
{
    private EventCacheOptions Options => options.Value;

    private DateTime Now => clock.GetUtcNow().UtcDateTime;

    /// <summary>The datasets that enabled, fully configured guilds read from. Equivalent guilds share one.</summary>
    public async Task<IReadOnlyList<EventDataset>> GetRequiredDatasetsAsync(CancellationToken cancellationToken = default)
    {
        var guilds = await db.GuildConfigurations
            .AsNoTracking()
            .Where(g => g.Enabled)
            .ToListAsync(cancellationToken);

        return EventDataset.ForGuilds(guilds);
    }

    /// <summary>
    /// Refreshes the dataset only when it is due. Concurrent callers for the same dataset make at most one request:
    /// freshness is checked again after the lock is acquired.
    /// </summary>
    public async Task<CacheRefreshResult> EnsureFreshAsync(EventDataset dataset, CancellationToken cancellationToken = default)
    {
        if (await GetSkipOutcomeAsync(dataset, cancellationToken) is { } skipped)
        {
            return Skip(dataset, skipped);
        }

        using (await locks.AcquireAsync(LockKey(dataset), cancellationToken))
        {
            // Another caller may have refreshed while this one waited.
            if (await GetSkipOutcomeAsync(dataset, cancellationToken) is { } skippedAfterWait)
            {
                return Skip(dataset, skippedAfterWait);
            }

            statistics.RecordCacheMiss();
            return await RefreshLockedAsync(dataset, cancellationToken);
        }
    }

    /// <summary>Refreshes now, ignoring freshness and backoff. Still one request at a time per dataset.</summary>
    public async Task<CacheRefreshResult> RefreshAsync(EventDataset dataset, CancellationToken cancellationToken = default)
    {
        using (await locks.AcquireAsync(LockKey(dataset), cancellationToken))
        {
            return await RefreshLockedAsync(dataset, cancellationToken);
        }
    }

    /// <summary>
    /// Wait before retrying after <paramref name="consecutiveFailures"/> failures, without jitter.
    /// Transient failures double from <see cref="EventCacheOptions.FailureRetryBase"/>; others wait the maximum.
    /// A longer <c>Retry-After</c> wins.
    /// </summary>
    public static TimeSpan ComputeBackoff(
        int consecutiveFailures, bool isTransient, TimeSpan? retryAfter, EventCacheOptions options)
    {
        var delay = options.FailureRetryMax;
        if (isTransient)
        {
            var doublings = Math.Clamp(consecutiveFailures - 1, 0, 20);
            var ticks = Math.Min(options.FailureRetryBase.Ticks * (1L << doublings), options.FailureRetryMax.Ticks);
            delay = TimeSpan.FromTicks(ticks);
        }

        if (retryAfter is { } wait && wait > delay)
        {
            delay = wait < EventCacheOptions.MaxRetryAfter ? wait : EventCacheOptions.MaxRetryAfter;
        }

        return delay;
    }

    private string LockKey(EventDataset dataset) => $"{provider.SourceName}:{dataset.Key}";

    private async Task<CacheRefreshOutcome?> GetSkipOutcomeAsync(EventDataset dataset, CancellationToken cancellationToken)
    {
        // Untracked so a refresh saved by another scope is always visible.
        var state = await db.DatasetSyncStates
            .AsNoTracking()
            .SingleOrDefaultAsync(s => s.Source == provider.SourceName && s.DatasetKey == dataset.Key, cancellationToken);

        if (state is null || state.IsDue(Now)) return null;
        return state.ConsecutiveFailures > 0 ? CacheRefreshOutcome.BackingOff : CacheRefreshOutcome.Fresh;
    }

    private CacheRefreshResult Skip(EventDataset dataset, CacheRefreshOutcome outcome)
    {
        statistics.RecordCacheHit();
        logger.LogDebug("{Source} cache for {CacheKey} not refreshed: {Outcome}", provider.SourceName, dataset.Key, outcome);
        return new CacheRefreshResult(dataset.Key, outcome);
    }

    private async Task<CacheRefreshResult> RefreshLockedAsync(EventDataset dataset, CancellationToken cancellationToken)
    {
        var startedAt = Now;
        var startTimestamp = clock.GetTimestamp();

        var state = await db.DatasetSyncStates
            .SingleOrDefaultAsync(s => s.Source == provider.SourceName && s.DatasetKey == dataset.Key, cancellationToken);
        if (state is null)
        {
            state = new DatasetSyncState { Source = provider.SourceName, DatasetKey = dataset.Key };
            db.DatasetSyncStates.Add(state);
        }

        var cacheAgeBefore = state.CacheAge(startedAt);

        // Persist the attempt first: if the process dies mid-request, the next start still waits.
        state.RecordAttempt(startedAt, startedAt + Options.FailureRetryBase);
        await db.SaveChangesAsync(cancellationToken);

        var criteria = dataset.ToCriteria(startedAt);
        statistics.RecordRequest();

        IReadOnlyCollection<PokemonEvent> fetched;
        try
        {
            fetched = await provider.GetEventsAsync(criteria, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return await RecordFailureAsync(dataset, state, ex, startedAt, startTimestamp, cancellationToken);
        }

        var result = new CacheRefreshResult(dataset.Key, CacheRefreshOutcome.Refreshed) { EventsReceived = fetched.Count };

        await UpsertEventsAsync(fetched, startedAt, result, cancellationToken);

        // Persist event data before missing-event detection, which queries LastSeenAt.
        await db.SaveChangesAsync(cancellationToken);

        if (fetched.Count == 0)
        {
            logger.LogWarning(
                "{Source} returned no events for {CacheKey}; skipping missing-event detection",
                provider.SourceName, dataset.Key);
        }
        else
        {
            await RecordMissingEventsAsync(dataset, criteria, startedAt, result, cancellationToken);
        }

        state.RecordSuccess(startedAt, fetched.Count, startedAt + Options.RefreshInterval + Jitter());
        await db.SaveChangesAsync(cancellationToken);
        statistics.RecordSuccess();

        logger.LogInformation(
            "{Source} refresh completed for {CacheKey} in {DurationMs} ms: {EventsReceived} received, {EventsAdded} added, " +
            "{EventsUpdated} updated, {EventsMissing} missing, {EventsRemoved} removed; cache age before {CacheAgeBefore}; " +
            "next refresh after {NextRefreshAt:o}",
            provider.SourceName, dataset.Key, (long)clock.GetElapsedTime(startTimestamp).TotalMilliseconds,
            result.EventsReceived, result.EventsAdded, result.EventsUpdated, result.EventsMissing, result.EventsRemoved,
            cacheAgeBefore?.ToString(@"d\.hh\:mm\:ss") ?? "none", state.NextAllowedRequestAt);

        return result;
    }

    private async Task<CacheRefreshResult> RecordFailureAsync(
        EventDataset dataset,
        DatasetSyncState state,
        Exception exception,
        DateTime startedAt,
        long startTimestamp,
        CancellationToken cancellationToken)
    {
        var providerError = exception as EventProviderException;
        var statusCode = providerError?.StatusCode;

        // Unexpected errors are treated as non-transient so they wait the maximum backoff.
        var delay = ComputeBackoff(
            state.ConsecutiveFailures + 1, providerError?.IsTransient ?? false, providerError?.RetryAfter, Options);

        state.RecordFailure(startedAt, exception.Message, statusCode, startedAt + delay + Jitter());
        await db.SaveChangesAsync(cancellationToken);
        statistics.RecordFailure(statusCode);

        logger.LogWarning(
            "{Source} refresh failed for {CacheKey} after {DurationMs} ms (HTTP {HttpStatus}, {ConsecutiveFailures} consecutive); " +
            "cached events kept, next attempt after {NextAttemptAt:o}: {Reason}",
            provider.SourceName, dataset.Key, (long)clock.GetElapsedTime(startTimestamp).TotalMilliseconds,
            statusCode?.ToString() ?? "none", state.ConsecutiveFailures, state.NextAllowedRequestAt, state.LastFailureReason);
        logger.LogDebug(exception, "{Source} refresh failure details for {CacheKey}", provider.SourceName, dataset.Key);

        return new CacheRefreshResult(dataset.Key, CacheRefreshOutcome.Failed);
    }

    private async Task UpsertEventsAsync(
        IReadOnlyCollection<PokemonEvent> fetched, DateTime now, CacheRefreshResult result, CancellationToken cancellationToken)
    {
        // The same source event may appear more than once in a response; last one wins.
        var incoming = fetched
            .GroupBy(e => (e.Source, e.SourceEventId))
            .Select(g => g.Last())
            .ToList();

        foreach (var sourceGroup in incoming.GroupBy(e => e.Source))
        {
            var ids = sourceGroup.Select(e => e.SourceEventId).ToList();
            var existing = await db.Events
                .Where(e => e.Source == sourceGroup.Key && ids.Contains(e.SourceEventId))
                .ToDictionaryAsync(e => e.SourceEventId, cancellationToken);

            foreach (var source in sourceGroup)
            {
                if (!existing.TryGetValue(source.SourceEventId, out var current))
                {
                    source.Id = Guid.NewGuid();
                    source.Status = EventStatus.Active;
                    source.FirstSeenAt = now;
                    source.LastSeenAt = now;
                    source.LastModifiedAt = now;
                    db.Events.Add(source);
                    result.EventsAdded++;

                    logger.LogDebug(
                        "New event discovered: {EventId} {Source}/{SourceEventId} '{EventName}' at {StartDateTime:o}",
                        source.Id, source.Source, source.SourceEventId, source.Name, source.StartDateTime);
                    continue;
                }

                var changes = current.ApplySourceData(source);
                changes.Merge(current.MarkSeen(now));

                if (!changes.HasChanges) continue;

                if (changes.IsMaterial)
                {
                    current.LastModifiedAt = now;
                    result.EventsUpdated++;
                    logger.LogDebug(
                        "Event updated: {EventId} {Source}/{SourceEventId}: {Changes}",
                        current.Id, current.Source, current.SourceEventId, changes.ToString());
                }
                else
                {
                    logger.LogDebug(
                        "Insignificant event change: {EventId} {Source}/{SourceEventId}: {Changes}",
                        current.Id, current.Source, current.SourceEventId, changes.ToString());
                }
            }
        }
    }

    /// <summary>
    /// Counts a miss for active upcoming events inside this dataset's area, window and types that the
    /// successful response did not list. Only called after a successful, non-empty response.
    /// </summary>
    private async Task RecordMissingEventsAsync(
        EventDataset dataset, EventSearchCriteria criteria, DateTime refreshedAt, CacheRefreshResult result,
        CancellationToken cancellationToken)
    {
        // Stop a day short of the window end: the source filters by venue-local date.
        var windowEnd = criteria.EndDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(-1);
        var types = criteria.EventTypes.ToList();
        var source = provider.SourceName;
        var game = criteria.Game;

        var query = db.Events.Where(e => e.Source == source
                                         && e.Status == EventStatus.Active
                                         && e.Game == game
                                         && e.LastSeenAt < refreshedAt
                                         && e.StartDateTime > refreshedAt
                                         && e.StartDateTime < windowEnd
                                         && types.Contains(e.EventType));

        if (dataset.UsesRadius)
        {
            query = query.WithinBox(GeoDistance.BoundingBox(
                dataset.Latitude!.Value, dataset.Longitude!.Value, dataset.Radius!.Value, dataset.RadiusUnit));
        }
        else
        {
            var country = dataset.Country;
            query = query.Where(e => e.Country == country);
        }

        var missing = (await query.ToListAsync(cancellationToken)).Where(dataset.Covers).ToList();

        foreach (var pokemonEvent in missing)
        {
            result.EventsMissing++;

            var changes = pokemonEvent.RecordMissing(refreshedAt, Options.MissingEventThreshold);
            if (!changes.IsMaterial) continue;

            pokemonEvent.LastModifiedAt = refreshedAt;
            result.EventsRemoved++;
            logger.LogWarning(
                "Event not listed by source for {MissingCount} successful refreshes, marked removed: {EventId} {Source}/{SourceEventId} '{EventName}'",
                pokemonEvent.MissingCount, pokemonEvent.Id, pokemonEvent.Source, pokemonEvent.SourceEventId, pokemonEvent.Name);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private TimeSpan Jitter() =>
        Options.JitterMinutes > 0 ? TimeSpan.FromMinutes(Random.Shared.NextDouble() * Options.JitterMinutes) : TimeSpan.Zero;
}
