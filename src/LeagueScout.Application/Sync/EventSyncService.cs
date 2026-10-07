using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using LeagueScout.Application.Caching;
using LeagueScout.Application.Persistence;
using LeagueScout.Application.Publishing;
using LeagueScout.Application.Rsvps;
using LeagueScout.Domain;

namespace LeagueScout.Application.Sync;

public sealed record SyncResult
{
    public int GuildsSynced { get; set; }
    public int GuildsFailed { get; set; }
    public int EventsMatched { get; set; }
    public int MessagesCreated { get; set; }
    public int MessagesUpdated { get; set; }
}

/// <summary>
/// Posts cached events to every enabled guild and edits posts whose event changed since they were last rendered.
/// Reads only the shared event cache; it never calls the event source (see <see cref="EventCacheService"/>).
/// Idempotent: re-running with an unchanged cache changes nothing and posts nothing.
/// </summary>
public class EventSyncService(
    IApplicationDbContext db,
    CachedEventReader cachedEvents,
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

        logger.LogInformation("Guild synchronization started for {GuildCount} enabled guild(s)", guilds.Count);

        // Edit existing posts first so a changed event is never posted again before its old post is updated.
        await UpdateChangedMessagesAsync(runStartedAt, result, cancellationToken);

        foreach (var guild in guilds)
        {
            var blocker = guild.GetSyncBlocker();
            if (blocker is not null)
            {
                logger.LogWarning("Skipping guild {GuildId}: {Reason}", guild.GuildId, blocker);
                continue;
            }

            try
            {
                await SyncGuildAsync(guild, runStartedAt, result, cancellationToken);
                result.GuildsSynced++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Guild synchronization failed for guild {GuildId}", guild.GuildId);
                result.GuildsFailed++;
            }
        }

        logger.LogInformation(
            "Guild synchronization completed: {GuildsSynced} guild(s) synced, {GuildsFailed} failed, {EventsMatched} cached events matched, " +
            "{MessagesCreated} messages created, {MessagesUpdated} messages updated",
            result.GuildsSynced, result.GuildsFailed, result.EventsMatched, result.MessagesCreated, result.MessagesUpdated);

        return result;
    }

    private async Task SyncGuildAsync(
        GuildConfiguration guild, DateTime runStartedAt, SyncResult result, CancellationToken cancellationToken)
    {
        var events = await cachedEvents.GetMatchingAsync(guild, runStartedAt, cancellationToken);
        result.EventsMatched += events.Count;
        logger.LogDebug("{EventCount} cached event(s) match guild {GuildId}", events.Count, guild.GuildId);

        await PostNewEventsAsync(guild, events, runStartedAt, result, cancellationToken);

        guild.LastSyncedAt = runStartedAt;
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Re-renders every post whose event changed materially (including removal) after the post was last rendered.</summary>
    private async Task UpdateChangedMessagesAsync(DateTime now, SyncResult result, CancellationToken cancellationToken)
    {
        var messages = await db.GuildEventMessages
            .Include(m => m.Event)
            .Where(m => m.Event.LastModifiedAt > m.LastUpdatedAt)
            .ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                var summary = await rsvpService.GetSummaryAsync(message.EventId, message.GuildId, cancellationToken);
                await publisher.UpdateAsync(message, message.Event, summary, cancellationToken);
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

            // Stamp even on failure: one attempt per change, so a deleted message is not retried every run.
            message.LastUpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task PostNewEventsAsync(
        GuildConfiguration guild, IReadOnlyList<PokemonEvent> events, DateTime now, SyncResult result, CancellationToken cancellationToken)
    {
        var alreadyPosted = await db.GuildEventMessages
            .Where(m => m.GuildId == guild.GuildId)
            .Select(m => m.EventId)
            .ToHashSetAsync(cancellationToken);

        var toPost = events.Where(e => !alreadyPosted.Contains(e.Id)).ToList();

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
