using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using LeagueScout.Application.Persistence;
using LeagueScout.Domain;

namespace LeagueScout.Application.Rsvps;

public sealed record RsvpResult(
    bool Succeeded,
    string? Error,
    PokemonEvent? Event = null,
    GuildEventMessage? Message = null,
    RsvpSummary? Summary = null)
{
    public static RsvpResult Fail(string error) => new(false, error);
}

public class RsvpService(IApplicationDbContext db, TimeProvider clock, ILogger<RsvpService> logger)
{
    /// <summary>How long after an event starts RSVPs are still accepted.</summary>
    private static readonly TimeSpan RsvpGracePeriod = TimeSpan.FromHours(12);

    /// <summary>
    /// Sets the user's RSVP for an event in a guild, replacing any existing status.
    /// The event must have been posted in that guild as <paramref name="messageId"/>.
    /// </summary>
    public async Task<RsvpResult> SetRsvpAsync(
        ulong guildId,
        ulong messageId,
        ulong userId,
        Guid eventId,
        RsvpStatus status,
        CancellationToken cancellationToken = default)
    {
        var message = await db.GuildEventMessages
            .Include(m => m.Event)
            .SingleOrDefaultAsync(m => m.GuildId == guildId && m.EventId == eventId, cancellationToken);

        if (message is null || message.MessageId != messageId)
        {
            logger.LogWarning(
                "Rejected RSVP for unknown event {EventId} in guild {GuildId}, message {MessageId}",
                eventId, guildId, messageId);
            return RsvpResult.Fail("This event isn't tracked in this server.");
        }

        var pokemonEvent = message.Event;
        var now = clock.GetUtcNow().UtcDateTime;

        if (pokemonEvent.Status == EventStatus.Removed)
        {
            return RsvpResult.Fail("This event is no longer listed and may have been cancelled.");
        }

        if (pokemonEvent.StartDateTime + RsvpGracePeriod < now)
        {
            return RsvpResult.Fail("This event has already happened.");
        }

        // A concurrent click from the same user can race the unique index; retry once.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await UpsertAsync(guildId, userId, eventId, status, now, cancellationToken);
                break;
            }
            catch (DbUpdateException) when (attempt == 1)
            {
                db.ChangeTracker.Clear();
            }
        }

        var summary = await GetSummaryAsync(eventId, guildId, cancellationToken);
        return new RsvpResult(true, null, pokemonEvent, message, summary);
    }

    public async Task<RsvpSummary> GetSummaryAsync(Guid eventId, ulong guildId, CancellationToken cancellationToken = default)
    {
        var counts = await db.EventRsvps
            .Where(r => r.EventId == eventId && r.GuildId == guildId)
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int CountOf(RsvpStatus s) => counts.SingleOrDefault(c => c.Status == s)?.Count ?? 0;

        return new RsvpSummary(CountOf(RsvpStatus.Interested), CountOf(RsvpStatus.Going), CountOf(RsvpStatus.NotGoing));
    }

    private async Task UpsertAsync(
        ulong guildId, ulong userId, Guid eventId, RsvpStatus status, DateTime now, CancellationToken cancellationToken)
    {
        var rsvp = await db.EventRsvps.SingleOrDefaultAsync(
            r => r.EventId == eventId && r.GuildId == guildId && r.DiscordUserId == userId,
            cancellationToken);

        if (rsvp is null)
        {
            db.EventRsvps.Add(new EventRsvp
            {
                EventId = eventId,
                GuildId = guildId,
                DiscordUserId = userId,
                Status = status,
                CreatedAt = now,
                UpdatedAt = now,
            });
        }
        else if (rsvp.Status == status)
        {
            return;
        }
        else
        {
            rsvp.Status = status;
            rsvp.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "RSVP updated: user {UserId} is {RsvpStatus} for event {EventId} in guild {GuildId}",
            userId, status, eventId, guildId);
    }
}
