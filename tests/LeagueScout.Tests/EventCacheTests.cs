using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeagueScout.Application.Caching;
using LeagueScout.Application.Providers;
using LeagueScout.Domain;
using LeagueScout.Infrastructure.PokeData;
using LeagueScout.Infrastructure.TimeZones;

namespace LeagueScout.Tests;

/// <summary>Shared event cache: freshness, request deduplication, backoff and missing-event handling.</summary>
public class EventCacheTests : IDisposable
{
    private readonly TestHarness _h = new();

    public void Dispose() => _h.Dispose();

    private static readonly EventProviderException ServiceUnavailable = new("PokéData returned HTTP 503.") { StatusCode = 503 };

    // Cache behaviour

    [Fact]
    public async Task Empty_cache_triggers_refresh_and_records_sync_state()
    {
        _h.Provider.Events = [_h.SourceEvent("26-10-000001"), _h.SourceEvent("26-10-000002")];

        var result = Assert.Single(await _h.EnsureFreshAsync());

        Assert.Equal(CacheRefreshOutcome.Refreshed, result.Outcome);
        Assert.Equal(2, result.EventsAdded);
        Assert.Equal(1, _h.Provider.Calls);

        var state = await _h.GetSyncStateAsync();
        Assert.NotNull(state);
        Assert.Equal("pokedata", state.Source);
        Assert.Equal(_h.Now, state.LastAttemptAt);
        Assert.Equal(_h.Now, state.LastSuccessAt);
        Assert.Equal(_h.Now + _h.CacheOptions.RefreshInterval, state.NextAllowedRequestAt);
        Assert.Equal(2, state.LastResultCount);
        Assert.Equal(0, state.ConsecutiveFailures);
    }

    [Fact]
    public async Task Fresh_cache_makes_no_upstream_request()
    {
        await _h.EnsureFreshAsync();
        _h.Clock.Advance(TimeSpan.FromHours(5));

        var result = Assert.Single(await _h.EnsureFreshAsync());

        Assert.Equal(CacheRefreshOutcome.Fresh, result.Outcome);
        Assert.Equal(1, _h.Provider.Calls);
        Assert.Equal(1, _h.Statistics.Snapshot().CacheHits);
    }

    [Fact]
    public async Task Stale_cache_triggers_refresh()
    {
        await _h.EnsureFreshAsync();
        _h.Clock.Advance(TimeSpan.FromHours(6));

        var result = Assert.Single(await _h.EnsureFreshAsync());

        Assert.Equal(CacheRefreshOutcome.Refreshed, result.Outcome);
        Assert.Equal(2, _h.Provider.Calls);
        Assert.Equal(_h.Now, (await _h.GetSyncStateAsync())!.LastSuccessAt);
    }

    [Fact]
    public async Task New_upstream_event_is_inserted_and_changed_event_is_updated()
    {
        _h.Provider.Events = [_h.SourceEvent("26-10-000001")];
        await _h.RefreshAsync();

        var changed = _h.SourceEvent("26-10-000001");
        changed.VenueName = "New Venue";
        _h.Provider.Events = [changed, _h.SourceEvent("26-10-000002")];
        _h.Clock.Advance(TimeSpan.FromHours(6));
        var result = await _h.RefreshAsync();

        Assert.Equal(1, result.EventsAdded);
        Assert.Equal(1, result.EventsUpdated);
        Assert.True(result.HasChanges);
        Assert.Equal("New Venue", (await _h.GetEventAsync("26-10-000001")).VenueName);
        Assert.Equal(_h.Now, (await _h.GetEventAsync("26-10-000002")).FirstSeenAt);
    }

    [Fact]
    public async Task Refresh_failure_preserves_cache_and_keeps_serving_it()
    {
        _h.Provider.Events = [_h.SourceEvent("26-10-000001"), _h.SourceEvent("26-10-000002")];
        await _h.SyncAsync();
        var lastSuccess = _h.Now;

        _h.Provider.Failure = ServiceUnavailable;
        _h.Clock.Advance(TimeSpan.FromHours(6));
        var result = Assert.Single(await _h.EnsureFreshAsync());

        Assert.Equal(CacheRefreshOutcome.Failed, result.Outcome);

        await using (var db = _h.Database.CreateContext())
        {
            var events = await db.Events.ToListAsync();
            Assert.Equal(2, events.Count);
            Assert.All(events, e => Assert.Equal(EventStatus.Active, e.Status));
            Assert.All(events, e => Assert.Equal(0, e.MissingCount));
        }

        var state = await _h.GetSyncStateAsync();
        Assert.Equal(lastSuccess, state!.LastSuccessAt);
        Assert.Equal(_h.Now, state.LastFailureAt);
        Assert.Equal(503, state.LastFailureStatusCode);
        Assert.Equal("PokéData returned HTTP 503.", state.LastFailureReason);

        // Guild commands and sync keep working from the cache.
        _h.AddGuild(2, g => g.Regions = ["Texas"]);
        var sync = await _h.GuildSyncAsync();
        Assert.Equal(2, sync.MessagesCreated);
        Assert.Empty(_h.Publisher.Updates);
    }

    // Failure backoff

    [Fact]
    public async Task Failures_back_off_exponentially_and_success_resets_the_count()
    {
        _h.Provider.Failure = ServiceUnavailable;

        foreach (var expectedMinutes in new[] { 15, 30, 60, 120, 120 })
        {
            var result = Assert.Single(await _h.EnsureFreshAsync());
            Assert.Equal(CacheRefreshOutcome.Failed, result.Outcome);

            var state = await _h.GetSyncStateAsync();
            Assert.Equal(_h.Now + TimeSpan.FromMinutes(expectedMinutes), state!.NextAllowedRequestAt);

            // Nothing is requested while backing off.
            var calls = _h.Provider.Calls;
            _h.Clock.Advance(TimeSpan.FromMinutes(expectedMinutes) - TimeSpan.FromSeconds(1));
            Assert.Equal(CacheRefreshOutcome.BackingOff, Assert.Single(await _h.EnsureFreshAsync()).Outcome);
            Assert.Equal(calls, _h.Provider.Calls);
            _h.Clock.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(5, (await _h.GetSyncStateAsync())!.ConsecutiveFailures);

        _h.Provider.Failure = null;
        Assert.Equal(CacheRefreshOutcome.Refreshed, Assert.Single(await _h.EnsureFreshAsync()).Outcome);

        var recovered = await _h.GetSyncStateAsync();
        Assert.Equal(0, recovered!.ConsecutiveFailures);
        Assert.Equal(_h.Now + _h.CacheOptions.RefreshInterval, recovered.NextAllowedRequestAt);
    }

    [Theory]
    [InlineData(1, true, null, 15)]
    [InlineData(2, true, null, 30)]
    [InlineData(3, true, null, 60)]
    [InlineData(4, true, null, 120)]
    [InlineData(9, true, null, 120)]
    [InlineData(1, false, null, 120)]
    [InlineData(1, true, 180, 180)]
    [InlineData(3, true, 5, 60)]
    [InlineData(1, true, 48 * 60, 24 * 60)]
    public void Backoff_follows_schedule_and_honours_retry_after(int failures, bool transient, int? retryAfterMinutes, int expectedMinutes)
    {
        var retryAfter = retryAfterMinutes is { } m ? TimeSpan.FromMinutes(m) : (TimeSpan?)null;

        var delay = EventCacheService.ComputeBackoff(failures, transient, retryAfter, new EventCacheOptions());

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), delay);
    }

    [Fact]
    public async Task Jitter_delays_the_next_request_by_at_most_the_configured_minutes()
    {
        _h.CacheOptions.JitterMinutes = 10;

        await _h.EnsureFreshAsync();

        var next = (await _h.GetSyncStateAsync())!.NextAllowedRequestAt;
        Assert.InRange(next, _h.Now + TimeSpan.FromHours(6), _h.Now + TimeSpan.FromHours(6) + TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task Http_429_backs_off_for_retry_after()
    {
        var handler = new StubHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromHours(1));
            return Task.FromResult(response);
        });

        var result = Assert.Single(await _h.EnsureFreshAsync(CreatePokeDataProvider(handler)));

        Assert.Equal(CacheRefreshOutcome.Failed, result.Outcome);
        var state = await _h.GetSyncStateAsync();
        Assert.Equal(429, state!.LastFailureStatusCode);
        Assert.Equal(_h.Now + TimeSpan.FromHours(1), state.NextAllowedRequestAt);
        Assert.Equal(1, _h.Statistics.Snapshot().RateLimited);

        // Not retried while waiting.
        _h.Clock.Advance(TimeSpan.FromMinutes(30));
        await _h.EnsureFreshAsync(CreatePokeDataProvider(handler));
        Assert.Equal(1, handler.Requests);
    }

    [Fact]
    public async Task Http_500_backs_off()
    {
        var handler = new StubHttpHandler(HttpStatusCode.InternalServerError, "oops");

        var result = Assert.Single(await _h.EnsureFreshAsync(CreatePokeDataProvider(handler)));

        Assert.Equal(CacheRefreshOutcome.Failed, result.Outcome);
        var state = await _h.GetSyncStateAsync();
        Assert.Equal(500, state!.LastFailureStatusCode);
        Assert.Equal(_h.Now + TimeSpan.FromMinutes(15), state.NextAllowedRequestAt);
        Assert.Equal(1, _h.Statistics.Snapshot().ServerErrors);
    }

    [Fact]
    public async Task Timeout_backs_off()
    {
        var handler = new StubHttpHandler(_ => throw new TaskCanceledException("timed out", new TimeoutException()));

        var result = Assert.Single(await _h.EnsureFreshAsync(CreatePokeDataProvider(handler)));

        Assert.Equal(CacheRefreshOutcome.Failed, result.Outcome);
        var state = await _h.GetSyncStateAsync();
        Assert.Null(state!.LastFailureStatusCode);
        Assert.Equal(1, state.ConsecutiveFailures);
        Assert.Equal(_h.Now + TimeSpan.FromMinutes(15), state.NextAllowedRequestAt);
    }

    [Fact]
    public async Task Unreadable_response_waits_the_maximum_backoff()
    {
        var handler = new StubHttpHandler(HttpStatusCode.OK, "<html>maintenance</html>");

        await _h.EnsureFreshAsync(CreatePokeDataProvider(handler));

        Assert.Equal(_h.Now + TimeSpan.FromHours(2), (await _h.GetSyncStateAsync())!.NextAllowedRequestAt);
    }

    [Fact]
    public async Task Failed_requests_do_not_purge_data_or_count_missing_events()
    {
        _h.Provider.Events = [_h.SourceEvent("26-10-000001"), _h.SourceEvent("26-10-000002")];
        await _h.RefreshAsync();

        _h.Provider.Failure = ServiceUnavailable;
        for (var i = 0; i < 3; i++)
        {
            _h.Clock.Advance(TimeSpan.FromHours(6));
            await _h.RefreshAsync();
        }

        await using var db = _h.Database.CreateContext();
        var events = await db.Events.ToListAsync();
        Assert.Equal(2, events.Count);
        Assert.All(events, e =>
        {
            Assert.Equal(EventStatus.Active, e.Status);
            Assert.Equal(0, e.MissingCount);
            Assert.Null(e.MissingSince);
        });
    }

    // Request deduplication and stampede protection

    [Fact]
    public async Task Concurrent_callers_for_one_dataset_make_one_request()
    {
        using var h = new TestHarness(fileBackedDatabase: true);
        h.Provider.Events = [h.SourceEvent("26-10-000001")];

        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        h.Provider.OnRequest = async () =>
        {
            requested.TrySetResult();
            await release.Task;
        };

        var callers = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
        {
            await using var db = h.Database.CreateContext();
            return await h.CreateCacheService(db).EnsureFreshAsync(TestHarness.UsDataset);
        })).ToList();

        await requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(100); // Let the other callers queue on the lock.
        release.SetResult();
        var results = await Task.WhenAll(callers);

        Assert.Equal(1, h.Provider.Calls);
        Assert.Single(results, r => r.Outcome == CacheRefreshOutcome.Refreshed);
        Assert.All(results.Where(r => r.Outcome != CacheRefreshOutcome.Refreshed),
            r => Assert.Equal(CacheRefreshOutcome.Fresh, r.Outcome));
    }

    [Fact]
    public async Task Freshness_is_rechecked_after_acquiring_the_lock()
    {
        // While this caller waits for the lock, another caller completes a refresh.
        var inner = new InProcessKeyedLock();
        _h.Locks = new CallbackLock(inner, async () =>
        {
            await using var db = _h.Database.CreateContext();
            var other = new EventCacheService(db, _h.Provider, inner, _h.Statistics,
                Options.Create(_h.CacheOptions), _h.Clock, NullLogger<EventCacheService>.Instance);
            await other.RefreshAsync(TestHarness.UsDataset);
        });

        var result = Assert.Single(await _h.EnsureFreshAsync());

        Assert.Equal(CacheRefreshOutcome.Fresh, result.Outcome);
        Assert.Equal(1, _h.Provider.Calls);
    }

    [Fact]
    public async Task Lock_is_released_when_the_refresh_is_cancelled()
    {
        using var cts = new CancellationTokenSource();
        _h.Provider.OnRequest = async () =>
        {
            await cts.CancelAsync();
            cts.Token.ThrowIfCancellationRequested();
        };

        await using (var db = _h.Database.CreateContext())
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => _h.CreateCacheService(db).EnsureFreshAsync(TestHarness.UsDataset, cts.Token));
        }

        _h.Provider.OnRequest = null;
        await using var db2 = _h.Database.CreateContext();
        var refresh = _h.CreateCacheService(db2).RefreshAsync(TestHarness.UsDataset);
        Assert.Same(refresh, await Task.WhenAny(refresh, Task.Delay(TimeSpan.FromSeconds(10))));
        Assert.Equal(CacheRefreshOutcome.Refreshed, (await refresh).Outcome);
    }

    // Event lifecycle

    [Fact]
    public async Task One_successful_missing_observation_does_not_remove_the_event()
    {
        _h.Provider.Events = [_h.SourceEvent("26-10-000001"), _h.SourceEvent("26-10-000002")];
        await _h.SyncAsync();

        _h.Provider.Events = [_h.SourceEvent("26-10-000001")];
        _h.Clock.Advance(TimeSpan.FromHours(6));
        var result = await _h.RefreshAsync();
        await _h.GuildSyncAsync();

        Assert.Equal(1, result.EventsMissing);
        Assert.Equal(0, result.EventsRemoved);
        Assert.Empty(_h.Publisher.Updates);

        var dropped = await _h.GetEventAsync("26-10-000002");
        Assert.Equal(EventStatus.Active, dropped.Status);
        Assert.Equal(1, dropped.MissingCount);
        Assert.Equal(_h.Now, dropped.MissingSince);
    }

    [Fact]
    public async Task Repeated_missing_observations_remove_the_event_and_reappearance_restores_it()
    {
        var kept = _h.SourceEvent("26-10-000001");
        var dropped = _h.SourceEvent("26-10-000002");
        _h.Provider.Events = [kept, dropped];
        await _h.SyncAsync();
        var firstMissingAt = _h.Now.AddHours(6);

        _h.Provider.Events = [kept];
        for (var i = 0; i < 2; i++)
        {
            _h.Clock.Advance(TimeSpan.FromHours(6));
            await _h.RefreshAsync();
        }

        var removedSync = await _h.GuildSyncAsync();

        var removed = await _h.GetEventAsync("26-10-000002");
        Assert.Equal(EventStatus.Removed, removed.Status);
        Assert.Equal(2, removed.MissingCount);
        Assert.Equal(firstMissingAt, removed.MissingSince);
        Assert.Equal(1, removedSync.MessagesUpdated);
        Assert.Equal(EventStatus.Removed, Assert.Single(_h.Publisher.Updates).Status);

        _h.Provider.Events = [kept, dropped];
        _h.Clock.Advance(TimeSpan.FromHours(6));
        await _h.SyncAsync();

        var restored = await _h.GetEventAsync("26-10-000002");
        Assert.Equal(EventStatus.Active, restored.Status);
        Assert.Equal(0, restored.MissingCount);
        Assert.Null(restored.MissingSince);
        Assert.Equal(2, _h.Publisher.Updates.Count);
        Assert.Equal(EventStatus.Active, _h.Publisher.Updates[^1].Status);
        Assert.Equal(2, _h.Publisher.Posts.Count);
    }

    [Fact]
    public async Task Reappearing_event_resets_the_missing_count()
    {
        var dropped = _h.SourceEvent("26-10-000002");
        _h.Provider.Events = [_h.SourceEvent("26-10-000001"), dropped];
        await _h.RefreshAsync();

        // Missing, back, missing: never two misses in a row.
        foreach (var listed in new[] { false, true, false })
        {
            _h.Provider.Events = listed ? [_h.SourceEvent("26-10-000001"), dropped] : [_h.SourceEvent("26-10-000001")];
            _h.Clock.Advance(TimeSpan.FromHours(6));
            await _h.RefreshAsync();
        }

        var stored = await _h.GetEventAsync("26-10-000002");
        Assert.Equal(EventStatus.Active, stored.Status);
        Assert.Equal(1, stored.MissingCount);
    }

    [Fact]
    public async Task Missing_threshold_is_configurable()
    {
        _h.CacheOptions.MissingEventThreshold = 1;
        _h.Provider.Events = [_h.SourceEvent("26-10-000001"), _h.SourceEvent("26-10-000002")];
        await _h.RefreshAsync();

        _h.Provider.Events = [_h.SourceEvent("26-10-000001")];
        _h.Clock.Advance(TimeSpan.FromHours(6));
        var result = await _h.RefreshAsync();

        Assert.Equal(1, result.EventsRemoved);
        Assert.Equal(EventStatus.Removed, (await _h.GetEventAsync("26-10-000002")).Status);
    }

    [Fact]
    public async Task Empty_response_does_not_count_missing_events()
    {
        _h.Provider.Events = [_h.SourceEvent("26-10-000001")];
        await _h.RefreshAsync();

        _h.Provider.Events = [];
        for (var i = 0; i < 3; i++)
        {
            _h.Clock.Advance(TimeSpan.FromHours(6));
            await _h.RefreshAsync();
        }

        var stored = await _h.GetEventAsync("26-10-000001");
        Assert.Equal(EventStatus.Active, stored.Status);
        Assert.Equal(0, stored.MissingCount);
    }

    [Fact]
    public async Task Events_outside_the_dataset_are_not_counted_missing()
    {
        // A Canadian event cached by another dataset is not expected in the US response.
        _h.Provider.Events = [_h.SourceEvent("26-10-000001"), _h.SourceEvent("26-10-000002", country: "CA", region: "Ontario")];
        await _h.RefreshAsync();

        _h.Provider.Events = [_h.SourceEvent("26-10-000001")];
        for (var i = 0; i < 2; i++)
        {
            _h.Clock.Advance(TimeSpan.FromHours(6));
            await _h.RefreshAsync();
        }

        var canadian = await _h.GetEventAsync("26-10-000002");
        Assert.Equal(EventStatus.Active, canadian.Status);
        Assert.Equal(0, canadian.MissingCount);
    }

    // Diagnostics

    [Fact]
    public async Task Diagnostics_report_dataset_health_and_guild_matches()
    {
        _h.Provider.Events = [_h.SourceEvent("26-10-000001"), _h.SourceEvent("26-10-000002", region: "Oklahoma")];
        await _h.EnsureFreshAsync();
        await _h.GuildSyncAsync();
        _h.Provider.Failure = ServiceUnavailable;
        _h.Clock.Advance(TimeSpan.FromHours(7));
        await _h.EnsureFreshAsync();

        await using var db = _h.Database.CreateContext();
        var service = new EventCacheDiagnosticsService(
            db, new CachedEventReader(db), _h.Provider, _h.Statistics, Options.Create(_h.CacheOptions), _h.Clock);
        var report = await service.GetAsync(TestHarness.GuildId);

        Assert.NotNull(report.Dataset);
        Assert.Equal("country:US", report.Dataset.DatasetKey);
        Assert.Equal(CacheHealth.Retrying, report.Dataset.Health);
        Assert.Equal(TimeSpan.FromHours(7), report.Dataset.CacheAge);
        Assert.Equal(1, report.Dataset.ConsecutiveFailures);
        Assert.Equal(503, report.Dataset.LastFailureStatusCode);
        Assert.Equal(2, report.CachedEventCount);
        Assert.Equal(2, report.UpcomingActiveEventCount);
        Assert.Equal(1, report.MatchingUpcomingEventCount);
        Assert.Equal(_h.Now.AddHours(-7), report.LastGuildSyncAt);
        Assert.Equal(2, report.Counters.UpstreamRequests);
        Assert.Equal(1, report.Counters.Failures);

        _h.Clock.Advance(TimeSpan.FromHours(2));
        Assert.Equal(CacheHealth.Stale, (await service.GetAsync(TestHarness.GuildId)).Dataset!.Health);
    }

    private PokeDataEventProvider CreatePokeDataProvider(HttpMessageHandler handler) =>
        new(new StubHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://pokedata.ovh/events2/") }),
            new GeoTimeZoneResolver(), Options.Create(new PokeDataOptions()), _h.Clock, NullLogger<PokeDataEventProvider>.Instance);

    /// <summary>Runs <paramref name="beforeAcquire"/> once, just before the first lock wait.</summary>
    private sealed class CallbackLock(IKeyedLock inner, Func<Task> beforeAcquire) : IKeyedLock
    {
        private Func<Task>? _pending = beforeAcquire;

        public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref _pending, null) is { } callback) await callback();
            return await inner.AcquireAsync(key, cancellationToken);
        }
    }
}
