using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using LeagueScout.Application.Persistence;
using LeagueScout.Application.Providers;
using LeagueScout.Application.Publishing;
using LeagueScout.Application.Rsvps;
using LeagueScout.Domain;

namespace LeagueScout.Application.Sync;

public sealed record SyncResult
{
    public int GuildsSynced { get; set; }
    public int GuildsFailed { get; set; }
    public int EventsRetrieved { get; set; }
    public int NewEvents { get; set; }
    public int UpdatedEvents { get; set; }
    public int RemovedEvents { get; set; }
    public int MessagesCreated { get; set; }
    public int MessagesUpdated { get; set; }
}

/// <summary>
/// Pulls events for every enabled guild, upserts them, posts new ones and refreshes changed ones.
/// Idempotent: re-running with unchanged source data changes nothing and posts nothing.
/// </summary>
public class EventSyncService(
    IApplicationDbContext db,
    IEventProvider provider,
    IEventMessagePublisher publisher,
    RsvpService rsvpService,
    TimeProvider clock,
    ILogger<EventSyncService> logger)
{
    public async Task<SyncResult> SyncAllAsync(CancellationToken cancellationToken = default)
    {
        var runStartedAt = clock.GetUtcNow().UtcDateTime;
        var result = new SyncResult();

        var guilds = await db.GuildConfigurations
            .Where(g => g.Enabled)
            .ToListAsync(cancellationToken);

        logger.LogInformation("Event synchronization started for {GuildCount} enabled guild(s)", guilds.Count);

        foreach (var guild in guilds)
        {
            var blocker = guild.GetSyncBlocker();
            if (blocker is not null)
            {
                logger.LogWarning("Skipping guild {GuildId}: {Reason}", guild.GuildId, blocker);
                continue;
            }

            if (await SyncGuildAsync(guild, runStartedAt, result, cancellationToken))
                result.GuildsSynced++;
            else
                result.GuildsFailed++;
        }

        logger.LogInformation(
            "Synchronization completed: {GuildsSynced} guild(s) synced, {GuildsFailed} failed, {EventsRetrieved} retrieved, " +
            "{NewEvents} new, {UpdatedEvents} updated, {RemovedEvents} removed, {MessagesCreated} messages created, {MessagesUpdated} messages updated",
            result.GuildsSynced, result.GuildsFailed, result.EventsRetrieved, result.NewEvents, result.UpdatedEvents,
            result.RemovedEvents, result.MessagesCreated, result.MessagesUpdated);

        return result;
    }

    private async Task<bool> SyncGuildAsync(
        GuildConfiguration guild, DateTime runStartedAt, SyncResult result, CancellationToken cancellationToken)
    {
        var criteria = EventSearchCriteria.ForGuild(guild, runStartedAt);

        IReadOnlyCollection<PokemonEvent> fetched;
        try
        {
            fetched = await provider.GetEventsAsync(criteria, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogError(ex, "Provider request failed for guild {GuildId}; existing data left unchanged", guild.GuildId);
            return false;
        }

        logger.LogInformation("Events retrieved: {EventCount} for guild {GuildId}", fetched.Count, guild.GuildId);
        result.EventsRetrieved += fetched.Count;

        var (events, materiallyChanged) = await UpsertEventsAsync(fetched, runStartedAt, result, cancellationToken);

        // Persist event data before removal detection (which queries LastSeenAt) and before touching Discord,
        // so a Discord failure never loses source data.
        await db.SaveChangesAsync(cancellationToken);

        if (fetched.Count == 0)
        {
            logger.LogWarning(
                "Provider returned no events for guild {GuildId}; skipping removal detection", guild.GuildId);
        }
        else
        {
            var removed = await MarkRemovedEventsAsync(guild, criteria, runStartedAt, cancellationToken);
            result.RemovedEvents += removed.Count;
            materiallyChanged.UnionWith(removed);
            await db.SaveChangesAsync(cancellationToken);
        }

        await UpdateChangedMessagesAsync(materiallyChanged, runStartedAt, result, cancellationToken);
        await PostNewEventsAsync(guild, events, runStartedAt, result, cancellationToken);

        return true;
    }

    private async Task<(List<PokemonEvent> Events, HashSet<Guid> MateriallyChanged)> UpsertEventsAsync(
        IReadOnlyCollection<PokemonEvent> fetched, DateTime now, SyncResult result, CancellationToken cancellationToken)
    {
        // The same source event may appear more than once in a response; last one wins.
        var incoming = fetched
            .GroupBy(e => (e.Source, e.SourceEventId))
            .Select(g => g.Last())
            .ToList();

        var events = new List<PokemonEvent>(incoming.Count);
        var materiallyChanged = new HashSet<Guid>();

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
                    events.Add(source);
                    result.NewEvents++;

                    logger.LogInformation(
                        "New event discovered: {EventId} {Source}/{SourceEventId} '{EventName}' at {StartDateTime:o}",
                        source.Id, source.Source, source.SourceEventId, source.Name, source.StartDateTime);
                    continue;
                }

                var changes = current.ApplySourceData(source);
                changes.Merge(current.MarkSeen(now));
                events.Add(current);

                if (!changes.HasChanges) continue;

                if (changes.IsMaterial)
                {
                    current.LastModifiedAt = now;
                    materiallyChanged.Add(current.Id);
                    result.UpdatedEvents++;
                    logger.LogInformation(
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

        return (events, materiallyChanged);
    }

    /// <summary>
    /// Marks upcoming events previously posted in this guild that the source no longer lists.
    /// Only events inside the searched window and type filter are considered.
    /// </summary>
    private async Task<List<Guid>> MarkRemovedEventsAsync(
        GuildConfiguration guild, EventSearchCriteria criteria, DateTime runStartedAt, CancellationToken cancellationToken)
    {
        // Stop a day short of the window end: the source filters by venue-local date.
        var windowEnd = criteria.EndDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddDays(-1);
        var types = criteria.EventTypes.ToList();

        var missing = await db.GuildEventMessages
            .Where(m => m.GuildId == guild.GuildId)
            .Select(m => m.Event)
            .Where(e => e.Status == EventStatus.Active
                        && e.LastSeenAt < runStartedAt
                        && e.StartDateTime > runStartedAt
                        && e.StartDateTime < windowEnd
                        && types.Contains(e.EventType))
            .ToListAsync(cancellationToken);

        var removed = new List<Guid>();
        foreach (var pokemonEvent in missing)
        {
            var changes = pokemonEvent.MarkRemoved();
            if (!changes.HasChanges) continue;

            pokemonEvent.LastModifiedAt = runStartedAt;
            removed.Add(pokemonEvent.Id);
            logger.LogWarning(
                "Event no longer listed by source, marked removed: {EventId} {Source}/{SourceEventId} '{EventName}'",
                pokemonEvent.Id, pokemonEvent.Source, pokemonEvent.SourceEventId, pokemonEvent.Name);
        }

        return removed;
    }

    private async Task UpdateChangedMessagesAsync(
        HashSet<Guid> eventIds, DateTime now, SyncResult result, CancellationToken cancellationToken)
    {
        if (eventIds.Count == 0) return;

        var ids = eventIds.ToList();
        var messages = await db.GuildEventMessages
            .Include(m => m.Event)
            .Where(m => ids.Contains(m.EventId))
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                var summary = await rsvpService.GetSummaryAsync(message.EventId, message.GuildId, cancellationToken);
                await publisher.UpdateAsync(message, message.Event, summary, cancellationToken);
                message.LastUpdatedAt = now;
                await db.SaveChangesAsync(cancellationToken);
                result.MessagesUpdated++;

                logger.LogInformation(
                    "Discord event message updated: message {MessageId} in guild {GuildId} for event {EventId}",
                    message.MessageId, message.GuildId, message.EventId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex,
                    "Failed to update Discord message {MessageId} in guild {GuildId} for event {EventId}",
                    message.MessageId, message.GuildId, message.EventId);
            }
        }
    }

    private async Task PostNewEventsAsync(
        GuildConfiguration guild, List<PokemonEvent> events, DateTime now, SyncResult result, CancellationToken cancellationToken)
    {
        var alreadyPosted = await db.GuildEventMessages
            .Where(m => m.GuildId == guild.GuildId)
            .Select(m => m.EventId)
            .ToHashSetAsync(cancellationToken);

        var toPost = events
            .Where(e => e.Status == EventStatus.Active && !e.HasStarted(now) && !alreadyPosted.Contains(e.Id))
            .OrderBy(e => e.StartDateTime)
            .ToList();

        foreach (var pokemonEvent in toPost)
        {
            ulong messageId;
            try
            {
                messageId = await publisher.PostAsync(
                    guild.GuildId, guild.EventChannelId!.Value, pokemonEvent, RsvpSummary.Empty, cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Stop for this guild; the next scheduled run retries the remaining events.
                logger.LogError(ex,
                    "Failed to post event {EventId} to channel {ChannelId} in guild {GuildId}; remaining events deferred",
                    pokemonEvent.Id, guild.EventChannelId, guild.GuildId);
                return;
            }

            // Save each mapping immediately so an interrupted run cannot re-post on the next run.
            db.GuildEventMessages.Add(new GuildEventMessage
            {
                GuildId = guild.GuildId,
                EventId = pokemonEvent.Id,
                ChannelId = guild.EventChannelId!.Value,
                MessageId = messageId,
                CreatedAt = now,
                LastUpdatedAt = now,
            });
            await db.SaveChangesAsync(cancellationToken);
            result.MessagesCreated++;

            logger.LogInformation(
                "Discord event message created: message {MessageId} in guild {GuildId} for event {EventId}",
                messageId, guild.GuildId, pokemonEvent.Id);
        }
    }
}
