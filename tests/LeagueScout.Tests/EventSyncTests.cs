using Microsoft.EntityFrameworkCore;
using LeagueScout.Application.Caching;
using LeagueScout.Application.Providers;
using LeagueScout.Domain;

namespace LeagueScout.Tests;

/// <summary>Guild synchronization: cached events matched to guilds and posted to Discord.</summary>
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
        var refresh = await _h.RefreshAsync();
        var sync = await _h.GuildSyncAsync();

        Assert.Equal(1, refresh.EventsUpdated);
        Assert.Equal(0, refresh.EventsAdded);
        Assert.Equal(0, sync.MessagesCreated);
        Assert.Equal(1, sync.MessagesUpdated);
        Assert.Single(_h.Publisher.Updates);

        var stored = await _h.GetEventAsync("26-10-000001");
        Assert.Equal(moved.StartDateTime, stored.StartDateTime);
        Assert.Equal(_h.Now, stored.LastModifiedAt);
        Assert.Equal(_h.Now, stored.LastSeenAt);

        // The post is now current; another sync does not edit it again.
        await _h.GuildSyncAsync();
        Assert.Single(_h.Publisher.Updates);
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
        var refresh = await _h.RefreshAsync();
        await _h.GuildSyncAsync();

        Assert.Equal(0, refresh.EventsUpdated);
        Assert.Empty(_h.Publisher.Updates);
        Assert.Equal("Renamed by organizer", (await _h.GetEventAsync("26-10-000001")).Name);
    }

    [Fact]
    public async Task Events_that_already_started_are_stored_but_not_posted()
    {
        _h.Provider.Events = [_h.SourceEvent("26-10-000001", start: _h.Now.AddHours(-1))];

        var refresh = await _h.RefreshAsync();
        await _h.GuildSyncAsync();

        Assert.Equal(1, refresh.EventsAdded);
        Assert.Empty(_h.Publisher.Posts);
    }

    [Fact]
    public async Task Guild_sync_reads_cache_and_never_calls_the_provider()
    {
        _h.Provider.Events = [_h.SourceEvent("26-10-000001")];
        await _h.RefreshAsync();
        var callsAfterRefresh = _h.Provider.Calls;

        // Even with the source down, guild sync posts from the cache.
        _h.Provider.Failure = new EventProviderException("PokéData returned HTTP 503.") { StatusCode = 503 };
        var result = await _h.GuildSyncAsync();
        await _h.GuildSyncAsync();

        Assert.Equal(callsAfterRefresh, _h.Provider.Calls);
        Assert.Equal(1, result.GuildsSynced);
        Assert.Equal(1, result.MessagesCreated);
        Assert.Single(_h.Publisher.Posts);
    }

    [Fact]
    public async Task Guild_sync_records_last_sync_time()
    {
        await _h.GuildSyncAsync();

        await using var db = _h.Database.CreateContext();
        Assert.Equal(_h.Now, (await db.GuildConfigurations.SingleAsync()).LastSyncedAt);
    }

    [Fact]
    public async Task Multiple_guilds_share_one_refresh_and_each_post_the_cached_events()
    {
        _h.AddGuild(2, g => g.Regions = ["Texas"]);
        _h.AddGuild(3, g => g.Regions = ["texas "]);
        _h.AddGuild(4, g => g.Regions = ["Oklahoma"]);
        _h.Provider.Events =
        [
            _h.SourceEvent("26-10-000001"),
            _h.SourceEvent("26-10-000002", region: "Oklahoma"),
        ];

        var refreshes = await _h.EnsureFreshAsync();
        var result = await _h.GuildSyncAsync();

        Assert.Equal(1, _h.Provider.Calls);
        Assert.Equal("country:US", Assert.Single(refreshes).DatasetKey);
        Assert.Equal(4, result.GuildsSynced);

        var texasEventId = (await _h.GetEventAsync("26-10-000001")).Id;
        var oklahomaEventId = (await _h.GetEventAsync("26-10-000002")).Id;
        Assert.Equal(
            [(2UL, texasEventId), (3UL, texasEventId), (4UL, oklahomaEventId), (TestHarness.GuildId, texasEventId)],
            _h.Publisher.Posts.Select(p => (p.GuildId, p.EventId)).OrderBy(p => p.GuildId));

        await using var db = _h.Database.CreateContext();
        Assert.Equal(2, await db.Events.CountAsync());
    }

    [Fact]
    public async Task Country_guild_without_regions_gets_every_event_in_the_country()
    {
        _h.AddGuild(2);
        _h.Provider.Events =
        [
            _h.SourceEvent("26-10-000001"),
            _h.SourceEvent("26-10-000002", region: "Oklahoma"),
            _h.SourceEvent("26-10-000003", region: "Ontario", country: "CA"),
        ];

        await _h.SyncAsync();

        Assert.Equal(2, _h.Publisher.Posts.Count(p => p.GuildId == 2));
    }

    [Fact]
    public async Task Event_type_and_look_ahead_filters_are_applied_locally()
    {
        _h.AddGuild(2, g =>
        {
            g.IncludeChallenges = false;
            g.LookAheadDays = 7;
        });
        _h.Provider.Events =
        [
            _h.SourceEvent("26-10-000001", start: _h.Now.AddDays(3)),
            _h.SourceEvent("26-10-000002", start: _h.Now.AddDays(3), type: PokemonEventType.Challenge),
            _h.SourceEvent("26-10-000003", start: _h.Now.AddDays(20)),
        ];

        await _h.SyncAsync();

        var cupId = (await _h.GetEventAsync("26-10-000001")).Id;
        Assert.Equal([cupId], _h.Publisher.Posts.Where(p => p.GuildId == 2).Select(p => p.EventId));
        Assert.Equal(3, _h.Publisher.Posts.Count(p => p.GuildId == TestHarness.GuildId));
    }

    [Fact]
    public async Task Radius_guild_gets_cached_events_within_its_radius()
    {
        _h.AddGuild(2, g =>
        {
            g.Country = null;
            g.Latitude = 32.7767;
            g.Longitude = -96.797;
            g.Radius = 50;
        });
        _h.Provider.Events =
        [
            _h.SourceEvent("26-10-000001", latitude: 32.95, longitude: -96.73), // Plano, ~13 mi
            _h.SourceEvent("26-10-000002", latitude: 29.76, longitude: -95.37), // Houston, ~225 mi
        ];

        var refreshes = await _h.EnsureFreshAsync();
        await _h.GuildSyncAsync();

        Assert.Equal(["country:US", "radius:32.78,-96.80,51mi"], refreshes.Select(r => r.DatasetKey));
        var planoId = (await _h.GetEventAsync("26-10-000001")).Id;
        Assert.Equal([planoId], _h.Publisher.Posts.Where(p => p.GuildId == 2).Select(p => p.EventId));
    }

    [Fact]
    public async Task Newly_configured_guild_posts_cached_events_without_a_request()
    {
        _h.Provider.Events = [_h.SourceEvent("26-10-000001")];
        await _h.EnsureFreshAsync();
        await _h.GuildSyncAsync();

        _h.AddGuild(2, g => g.Regions = ["Texas"]);
        await _h.EnsureFreshAsync();
        await _h.GuildSyncAsync();

        Assert.Equal(1, _h.Provider.Calls);
        Assert.Single(_h.Publisher.Posts, p => p.GuildId == 2);
    }

    [Fact]
    public async Task Disabled_guilds_need_no_dataset_and_cause_no_request()
    {
        await using (var db = _h.Database.CreateContext())
        {
            (await db.GuildConfigurations.SingleAsync()).Enabled = false;
            await db.SaveChangesAsync();
        }

        _h.AddGuild(2, g =>
        {
            g.Country = "CA";
            g.EventChannelId = null; // Enabled but incomplete.
        });

        _h.Provider.Events = [_h.SourceEvent("26-10-000001")];
        var refreshes = await _h.EnsureFreshAsync();
        await _h.GuildSyncAsync();

        Assert.Empty(refreshes);
        Assert.Equal(0, _h.Provider.Calls);
        Assert.Empty(_h.Publisher.Posts);
    }

    [Fact]
    public void Equivalent_guild_configurations_map_to_one_dataset_key()
    {
        static GuildConfiguration Guild(Action<GuildConfiguration> configure)
        {
            var guild = new GuildConfiguration { EventChannelId = 1, Enabled = true };
            configure(guild);
            return guild;
        }

        var datasets = EventDataset.ForGuilds(
        [
            Guild(g => { g.Country = "US"; g.Regions = ["Texas"]; }),
            Guild(g => { g.Country = "us "; g.Regions = ["Texas"]; g.LookAheadDays = 60; }),
            Guild(g => { g.Country = "US"; g.Regions = ["Oklahoma"]; g.IncludeCups = false; }),
            Guild(g => { g.Latitude = 32.77671; g.Longitude = -96.79702; g.Radius = 50; }),
            Guild(g => { g.Latitude = 32.7801; g.Longitude = -96.7951; g.Radius = 49.5; }),
            Guild(g => { g.Country = "GB"; g.Enabled = false; }),
        ]);

        Assert.Equal(["country:US", "radius:32.78,-96.80,51mi"], datasets.Select(d => d.Key));
        Assert.Equal(60, datasets[0].LookAheadDays);
    }
}
