using Microsoft.EntityFrameworkCore;
using LeagueScout.Application.Persistence;
using LeagueScout.Domain;

namespace LeagueScout.Application.Queries;

public sealed record GuildEventListing(PokemonEvent Event, GuildEventMessage Message, RsvpStatus? UserStatus = null);

public class EventQueryService(IApplicationDbContext db, TimeProvider clock)
{
    /// <summary>Upcoming active events posted in the guild, soonest first.</summary>
    public async Task<IReadOnlyList<GuildEventListing>> GetUpcomingAsync(
        ulong guildId, int limit, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var messages = await db.GuildEventMessages
            .Include(m => m.Event)
            .Where(m => m.GuildId == guildId
                        && m.Event.Status == EventStatus.Active
                        && m.Event.StartDateTime >= now)
            .OrderBy(m => m.Event.StartDateTime)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return messages.Select(m => new GuildEventListing(m.Event, m)).ToList();
    }

    /// <summary>Upcoming events the user marked Interested or Going in the guild, soonest first.</summary>
    public async Task<IReadOnlyList<GuildEventListing>> GetUserEventsAsync(
        ulong guildId, ulong userId, int limit, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;

        var rows = await (
                from r in db.EventRsvps
                join m in db.GuildEventMessages on new { r.EventId, r.GuildId } equals new { m.EventId, m.GuildId }
                where r.GuildId == guildId
                      && r.DiscordUserId == userId
                      && (r.Status == RsvpStatus.Interested || r.Status == RsvpStatus.Going)
                      && r.Event.StartDateTime >= now
                orderby r.Event.StartDateTime
                select new { r.Status, Message = m, r.Event })
            .Take(limit)
            .ToListAsync(cancellationToken);

        return rows.Select(x => new GuildEventListing(x.Event, x.Message, x.Status)).ToList();
    }
}
