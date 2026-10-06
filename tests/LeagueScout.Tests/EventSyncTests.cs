using Microsoft.EntityFrameworkCore;
using LeagueScout.Application.Providers;
using LeagueScout.Domain;

namespace LeagueScout.Tests;

public class EventSyncTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    [Fact]
    public async Task Same_source_event_returned_twice_does_not_create_duplicate_events()
    {
        _h.Provider.Events = [_h.SourceEvent("26-10-000001"), _h.SourceEvent("26-10-000001")];

        await _h.SyncAsync();
        await _h.SyncAsync();

        await using var db = _h.Database.CreateContext();
        Assert.Equal(1, await db.Events.CountAsync());
    }

    [Fact]
    public async Task Running_sync_twice_does_not_create_duplicate_messages()
    {
        _h.Provider.Events = [_h.SourceEvent("26-10-000001"), _h.SourceEvent("26-10-000002")];

        var first = await _h.SyncAsync();
        var second = await _h.SyncAsync();

        Assert.Equal(2, first.MessagesCreated);
        Assert.Equal(0, second.MessagesCreated);
        Assert.Equal(0, second.MessagesUpdated);
        Assert.Equal(2, _h.Publisher.Posts.Count);
        Assert.Empty(_h.Publisher.Updates);

        await using var db = _h.Database.CreateContext();
        Assert.Equal(2, await db.GuildEventMessages.CountAsync());
    }

    [Fact]
    public async Task Material_change_updates_existing_event_and_message()
    {
        var original = _h.SourceEvent("26-10-000001");
        _h.Provider.Events = [original];
        await _h.SyncAsync();

        var moved = _h.SourceEvent("26-10-000001", start: original.StartDateTime.AddHours(2));
        _h.Provider.Events = [moved];
        _h.Clock.Advance(TimeSpan.FromHours(6));
        var result = await _h.SyncAsync();

        Assert.Equal(1, result.UpdatedEvents);
        Assert.Equal(0, result.NewEvents);
        Assert.Equal(0, result.MessagesCreated);
        Assert.Single(_h.Publisher.Updates);

        await using var db = _h.Database.CreateContext();
        var stored = await db.Events.SingleAsync();
        Assert.Equal(moved.StartDateTime, stored.StartDateTime);
        Assert.Equal(_h.Now, stored.LastModifiedAt);
        Assert.Equal(_h.Now, stored.LastSeenAt);
    }

    [Fact]
    public async Task Insignificant_change_is_saved_without_touching_discord()
    {
        _h.Provider.Events = [_h.SourceEvent("26-10-000001")];
        await _h.SyncAsync();

        var renamed = _h.SourceEvent("26-10-000001");
        renamed.Name = "Renamed by organizer";
        renamed.Latitude = 32.1;
        _h.Provider.Events = [renamed];
        var result = await _h.SyncAsync();

        Assert.Equal(0, result.UpdatedEvents);
        Assert.Empty(_h.Publisher.Updates);

        await using var db = _h.Database.CreateContext();
        Assert.Equal("Renamed by organizer", (await db.Events.SingleAsync()).Name);
    }

    [Fact]
    public async Task Provider_failure_does_not_change_existing_data()
    {
        _h.Provider.Events = [_h.SourceEvent("26-10-000001"), _h.SourceEvent("26-10-000002")];
        await _h.SyncAsync();

        await using (var before = _h.Database.CreateContext())
        {
            Assert.All(await before.Events.ToListAsync(), e => Assert.Equal(EventStatus.Active, e.Status));
        }

        _h.Provider.Failure = new EventProviderException("PokéData request failed: 503");
        _h.Clock.Advance(TimeSpan.FromHours(6));
        var result = await _h.SyncAsync();

        Assert.Equal(1, result.GuildsFailed);
        Assert.Equal(2, _h.Publisher.Posts.Count);
        Assert.Empty(_h.Publisher.Updates);

        await using var db = _h.Database.CreateContext();
        var events = await db.Events.ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, e => Assert.Equal(EventStatus.Active, e.Status));
        Assert.Equal(2, await db.GuildEventMessages.CountAsync());
    }

    [Fact]
    public async Task Event_missing_from_source_is_marked_removed_and_restored_when_it_returns()
    {
        var kept = _h.SourceEvent("26-10-000001");
        var dropped = _h.SourceEvent("26-10-000002");
        _h.Provider.Events = [kept, dropped];
        await _h.SyncAsync();

        _h.Provider.Events = [kept];
        _h.Clock.Advance(TimeSpan.FromHours(6));
        var removedRun = await _h.SyncAsync();

        Assert.Equal(1, removedRun.RemovedEvents);
        var update = Assert.Single(_h.Publisher.Updates);
        Assert.Equal(EventStatus.Removed, update.Status);

        _h.Provider.Events = [kept, dropped];
        _h.Clock.Advance(TimeSpan.FromHours(6));
        await _h.SyncAsync();

        Assert.Equal(2, _h.Publisher.Updates.Count);
        Assert.Equal(EventStatus.Active, _h.Publisher.Updates[^1].Status);
        Assert.Equal(2, _h.Publisher.Posts.Count);
    }

    [Fact]
    public async Task Empty_source_result_does_not_mark_events_removed()
    {
        _h.Provider.Events = [_h.SourceEvent("26-10-000001")];
        await _h.SyncAsync();

        _h.Provider.Events = [];
        _h.Clock.Advance(TimeSpan.FromHours(6));
        var result = await _h.SyncAsync();

        Assert.Equal(0, result.RemovedEvents);
        await using var db = _h.Database.CreateContext();
        Assert.Equal(EventStatus.Active, (await db.Events.SingleAsync()).Status);
    }

    [Fact]
    public async Task Events_that_already_started_are_stored_but_not_posted()
    {
        _h.Provider.Events = [_h.SourceEvent("26-10-000001", start: _h.Now.AddHours(-1))];

        var result = await _h.SyncAsync();

        Assert.Equal(1, result.NewEvents);
        Assert.Empty(_h.Publisher.Posts);
    }

    [Fact]
    public async Task Disabled_guild_is_not_synced()
    {
        await using (var db = _h.Database.CreateContext())
        {
            (await db.GuildConfigurations.SingleAsync()).Enabled = false;
            await db.SaveChangesAsync();
        }

        _h.Provider.Events = [_h.SourceEvent("26-10-000001")];
        await _h.SyncAsync();

        Assert.Equal(0, _h.Provider.Calls);
    }
}
