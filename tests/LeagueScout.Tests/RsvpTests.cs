using Microsoft.EntityFrameworkCore;
using LeagueScout.Domain;

namespace LeagueScout.Tests;

public class RsvpTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private async Task<Guid> PostEventAsync(string id = "26-10-000001")
    {
        _h.Provider.Events.Add(_h.SourceEvent(id));
        await _h.SyncAsync();
        await using var db = _h.Database.CreateContext();
        return (await db.Events.SingleAsync(e => e.SourceEventId == id)).Id;
    }

    [Fact]
    public async Task Changing_from_interested_to_going_updates_the_existing_rsvp()
    {
        var eventId = await PostEventAsync();

        await _h.RsvpAsync(userId: 1, eventId, RsvpStatus.Interested);
        _h.Clock.Advance(TimeSpan.FromMinutes(5));
        var result = await _h.RsvpAsync(userId: 1, eventId, RsvpStatus.Going);

        Assert.True(result.Succeeded);
        Assert.Equal(new(0, 1, 0), result.Summary);

        await using var db = _h.Database.CreateContext();
        var rsvp = await db.EventRsvps.SingleAsync();
        Assert.Equal(RsvpStatus.Going, rsvp.Status);
        Assert.True(rsvp.UpdatedAt > rsvp.CreatedAt);
    }

    [Theory]
    [InlineData(RsvpStatus.Interested, RsvpStatus.Going)]
    [InlineData(RsvpStatus.Going, RsvpStatus.NotGoing)]
    [InlineData(RsvpStatus.NotGoing, RsvpStatus.Interested)]
    public async Task Any_status_can_change_to_another(RsvpStatus from, RsvpStatus to)
    {
        var eventId = await PostEventAsync();

        await _h.RsvpAsync(userId: 1, eventId, from);
        await _h.RsvpAsync(userId: 1, eventId, to);

        await using var db = _h.Database.CreateContext();
        Assert.Equal(to, (await db.EventRsvps.SingleAsync()).Status);
    }

    [Fact]
    public async Task Different_users_rsvp_independently()
    {
        var eventId = await PostEventAsync();

        await _h.RsvpAsync(userId: 1, eventId, RsvpStatus.Going);
        await _h.RsvpAsync(userId: 2, eventId, RsvpStatus.Interested);
        var result = await _h.RsvpAsync(userId: 3, eventId, RsvpStatus.Going);

        Assert.Equal(new(1, 2, 0), result.Summary);

        await using var db = _h.Database.CreateContext();
        Assert.Equal(3, await db.EventRsvps.CountAsync());
    }

    [Fact]
    public async Task Rsvp_for_event_not_posted_in_guild_is_rejected()
    {
        var eventId = await PostEventAsync();

        var otherGuild = await _h.RsvpAsync(userId: 1, eventId, RsvpStatus.Going, messageId: 1000, guildId: 999);
        var unknownEvent = await _h.RsvpAsync(userId: 1, Guid.NewGuid(), RsvpStatus.Going, messageId: 1000);
        var wrongMessage = await _h.RsvpAsync(userId: 1, eventId, RsvpStatus.Going, messageId: 424242);

        Assert.False(otherGuild.Succeeded);
        Assert.False(unknownEvent.Succeeded);
        Assert.False(wrongMessage.Succeeded);

        await using var db = _h.Database.CreateContext();
        Assert.Equal(0, await db.EventRsvps.CountAsync());
    }

    [Fact]
    public async Task Rsvp_for_removed_event_is_rejected()
    {
        var eventId = await PostEventAsync();
        await using (var db = _h.Database.CreateContext())
        {
            (await db.Events.SingleAsync()).Status = EventStatus.Removed;
            await db.SaveChangesAsync();
        }

        var result = await _h.RsvpAsync(userId: 1, eventId, RsvpStatus.Going);

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Rsvp_counts_survive_event_updates()
    {
        var eventId = await PostEventAsync();
        await _h.RsvpAsync(userId: 1, eventId, RsvpStatus.Going);

        var moved = _h.SourceEvent("26-10-000001", start: _h.Now.AddDays(11));
        _h.Provider.Events = [moved];
        await _h.SyncAsync();

        var update = Assert.Single(_h.Publisher.Updates);
        Assert.Equal(new(0, 1, 0), update.Rsvps);
    }
}
