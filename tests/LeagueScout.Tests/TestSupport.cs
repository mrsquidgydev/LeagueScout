using System.Net;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using LeagueScout.Application.Caching;
using LeagueScout.Application.Providers;
using LeagueScout.Application.Publishing;
using LeagueScout.Application.Rsvps;
using LeagueScout.Application.Sync;
using LeagueScout.Domain;
using LeagueScout.Infrastructure.Persistence;

namespace LeagueScout.Tests;

/// <summary>
/// SQLite database with the real migrations and constraints applied. In memory by default;
/// file-backed when contexts must be used from several threads at once.
/// </summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection? _connection;
    private readonly string? _path;

    public TestDatabase(bool fileBacked = false)
    {
        if (fileBacked)
        {
            _path = Path.Combine(Path.GetTempPath(), $"leaguescout-test-{Guid.NewGuid():N}.db");
        }
        else
        {
            _connection = new SqliteConnection("Data Source=:memory:");
            _connection.Open();
        }

        using var db = CreateContext();
        db.Database.Migrate();
    }

    public AppDbContext CreateContext()
    {
        var builder = new DbContextOptionsBuilder<AppDbContext>();
        if (_connection is not null) builder.UseSqlite(_connection);
        else builder.UseSqlite($"Data Source={_path}");
        return new AppDbContext(builder.Options);
    }

    public void Dispose()
    {
        _connection?.Dispose();
        if (_path is null) return;

        SqliteConnection.ClearAllPools();
        File.Delete(_path);
    }
}

public sealed class FakeEventProvider : IEventProvider
{
    private readonly List<EventSearchCriteria> _criteria = [];

    public string SourceName => "pokedata";

    public List<PokemonEvent> Events { get; set; } = [];
    public Exception? Failure { get; set; }

    /// <summary>Runs on every request before it returns, e.g. to hold concurrent callers.</summary>
    public Func<Task>? OnRequest { get; set; }

    public int Calls
    {
        get { lock (_criteria) return _criteria.Count; }
    }

    public IReadOnlyList<EventSearchCriteria> Criteria
    {
        get { lock (_criteria) return _criteria.ToList(); }
    }

    public async Task<IReadOnlyCollection<PokemonEvent>> GetEventsAsync(EventSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        lock (_criteria) _criteria.Add(criteria);
        if (OnRequest is not null) await OnRequest();
        if (Failure is not null) throw Failure;

        // Return fresh copies, as a real provider would.
        return Events.Select(Copy).ToList();
    }

    private static PokemonEvent Copy(PokemonEvent e) => new()
    {
        Source = e.Source,
        SourceEventId = e.SourceEventId,
        Name = e.Name,
        Game = e.Game,
        EventType = e.EventType,
        StartDateTime = e.StartDateTime,
        EndDateTime = e.EndDateTime,
        TimeZoneId = e.TimeZoneId,
        VenueName = e.VenueName,
        Address = e.Address,
        City = e.City,
        Region = e.Region,
        PostalCode = e.PostalCode,
        Country = e.Country,
        Latitude = e.Latitude,
        Longitude = e.Longitude,
        RegistrationUrl = e.RegistrationUrl,
        SourceUrl = e.SourceUrl,
    };
}

public sealed class FakePublisher : IEventMessagePublisher
{
    private ulong _nextMessageId = 1000;

    public List<(ulong GuildId, ulong ChannelId, Guid EventId, ulong MessageId)> Posts { get; } = [];
    public List<(ulong MessageId, Guid EventId, EventStatus Status, RsvpSummary Rsvps)> Updates { get; } = [];

    public Task<ulong> PostAsync(ulong guildId, ulong channelId, PokemonEvent pokemonEvent, RsvpSummary rsvps, CancellationToken cancellationToken = default)
    {
        var id = _nextMessageId++;
        Posts.Add((guildId, channelId, pokemonEvent.Id, id));
        return Task.FromResult(id);
    }

    public Task UpdateAsync(GuildEventMessage message, PokemonEvent pokemonEvent, RsvpSummary rsvps, CancellationToken cancellationToken = default)
    {
        Updates.Add((message.MessageId, pokemonEvent.Id, pokemonEvent.Status, rsvps));
        return Task.CompletedTask;
    }
}

/// <summary>Returns a canned response, or runs a function, for every request.</summary>
public sealed class StubHttpHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
{
    public int Requests { get; private set; }

    public StubHttpHandler(HttpStatusCode status, string body)
        : this(_ => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        }))
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests++;
        return respond(request);
    }
}

public sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => client;
}

/// <summary>Wires the application services against a test database and fakes.</summary>
public sealed class TestHarness : IDisposable
{
    public const ulong GuildId = 111;
    public const ulong ChannelId = 222;

    public TestDatabase Database { get; }
    public FakeEventProvider Provider { get; } = new();
    public FakePublisher Publisher { get; } = new();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

    /// <summary>No jitter, so schedules are exact.</summary>
    public EventCacheOptions CacheOptions { get; } = new() { JitterMinutes = 0 };

    public IKeyedLock Locks { get; set; } = new InProcessKeyedLock();
    public EventCacheStatistics Statistics { get; } = new();

    public DateTime Now => Clock.GetUtcNow().UtcDateTime;

    /// <summary>The dataset the default Texas guild reads from.</summary>
    public static EventDataset UsDataset => EventDataset.ForGuild(new GuildConfiguration { Country = "US" })!;

    public TestHarness(bool fileBackedDatabase = false)
    {
        Database = new TestDatabase(fileBackedDatabase);
        AddGuild(GuildId, g => g.Regions = ["Texas"]);
    }

    /// <summary>Adds an enabled US guild posting to <see cref="ChannelId"/>; <paramref name="configure"/> adjusts it.</summary>
    public void AddGuild(ulong guildId, Action<GuildConfiguration>? configure = null)
    {
        var guild = new GuildConfiguration
        {
            GuildId = guildId,
            EventChannelId = ChannelId,
            Country = "US",
            Enabled = true,
            LookAheadDays = 30,
        };
        configure?.Invoke(guild);

        using var db = Database.CreateContext();
        db.GuildConfigurations.Add(guild);
        db.SaveChanges();
    }

    public EventCacheService CreateCacheService(AppDbContext db, IEventProvider? provider = null) =>
        new(db, provider ?? Provider, Locks, Statistics, Options.Create(CacheOptions), Clock, NullLogger<EventCacheService>.Instance);

    /// <summary>What the refresh worker does on each check: refresh every needed dataset that is due.</summary>
    public async Task<IReadOnlyList<CacheRefreshResult>> EnsureFreshAsync(IEventProvider? provider = null)
    {
        await using var db = Database.CreateContext();
        var cache = CreateCacheService(db, provider);
        var results = new List<CacheRefreshResult>();
        foreach (var dataset in await cache.GetRequiredDatasetsAsync())
        {
            results.Add(await cache.EnsureFreshAsync(dataset));
        }

        return results;
    }

    /// <summary>Refreshes the default guild's dataset now, ignoring freshness.</summary>
    public async Task<CacheRefreshResult> RefreshAsync()
    {
        await using var db = Database.CreateContext();
        return await CreateCacheService(db).RefreshAsync(UsDataset);
    }

    /// <summary>Guild synchronization only: reads the cache and posts.</summary>
    public async Task<SyncResult> GuildSyncAsync()
    {
        await using var db = Database.CreateContext();
        var rsvps = new RsvpService(db, Clock, NullLogger<RsvpService>.Instance);
        var sync = new EventSyncService(db, new CachedEventReader(db), Publisher, rsvps, Clock, NullLogger<EventSyncService>.Instance);
        return await sync.SyncAllAsync();
    }

    /// <summary>A cache refresh followed by guild synchronization.</summary>
    public async Task<SyncResult> SyncAsync()
    {
        await RefreshAsync();
        return await GuildSyncAsync();
    }

    public async Task<DatasetSyncState?> GetSyncStateAsync(string datasetKey = "country:US")
    {
        await using var db = Database.CreateContext();
        return await db.DatasetSyncStates.SingleOrDefaultAsync(s => s.DatasetKey == datasetKey);
    }

    public async Task<PokemonEvent> GetEventAsync(string sourceEventId)
    {
        await using var db = Database.CreateContext();
        return await db.Events.SingleAsync(e => e.SourceEventId == sourceEventId);
    }

    public async Task<RsvpResult> RsvpAsync(ulong userId, Guid eventId, RsvpStatus status, ulong? messageId = null, ulong guildId = GuildId)
    {
        await using var db = Database.CreateContext();
        messageId ??= (await db.GuildEventMessages.SingleAsync(m => m.EventId == eventId && m.GuildId == guildId)).MessageId;
        var rsvps = new RsvpService(db, Clock, NullLogger<RsvpService>.Instance);
        return await rsvps.SetRsvpAsync(guildId, messageId.Value, userId, eventId, status);
    }

    public PokemonEvent SourceEvent(
        string id,
        DateTime? start = null,
        PokemonEventType type = PokemonEventType.Cup,
        string region = "Texas",
        string country = "US",
        double? latitude = null,
        double? longitude = null) => new()
    {
        Source = "pokedata",
        SourceEventId = id,
        Name = $"League Cup {id}",
        Game = PokemonGame.TCG,
        EventType = type,
        StartDateTime = start ?? Now.AddDays(10),
        VenueName = "Example Games",
        City = "Dallas",
        Region = region,
        Country = country,
        Latitude = latitude,
        Longitude = longitude,
        RegistrationUrl = $"https://www.pokemon.com/us/pokemon-trainer-club/play-pokemon-tournaments/{id}/",
        SourceUrl = "https://pokedata.ovh/events2/",
    };

    public void Dispose() => Database.Dispose();
}
