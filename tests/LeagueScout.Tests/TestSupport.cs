using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using LeagueScout.Application.Providers;
using LeagueScout.Application.Publishing;
using LeagueScout.Application.Rsvps;
using LeagueScout.Application.Sync;
using LeagueScout.Domain;
using LeagueScout.Infrastructure.Persistence;

namespace LeagueScout.Tests;

/// <summary>In-memory SQLite database with the real migrations and constraints applied.</summary>
public sealed class TestDatabase : IDisposable
{
    private readonly SqliteConnection _connection;

    public TestDatabase()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        using var db = CreateContext();
        db.Database.Migrate();
    }

    public AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);

    public void Dispose() => _connection.Dispose();
}

public sealed class FakeEventProvider : IEventProvider
{
    public string SourceName => "pokedata";

    public List<PokemonEvent> Events { get; set; } = [];
    public Exception? Failure { get; set; }
    public int Calls { get; private set; }

    public Task<IReadOnlyCollection<PokemonEvent>> GetEventsAsync(EventSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        Calls++;
        if (Failure is not null) throw Failure;

        // Return fresh copies, as a real provider would.
        IReadOnlyCollection<PokemonEvent> copies = Events.Select(Copy).ToList();
        return Task.FromResult(copies);
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

/// <summary>Wires the application services against a test database and fakes.</summary>
public sealed class TestHarness : IDisposable
{
    public const ulong GuildId = 111;
    public const ulong ChannelId = 222;

    public TestDatabase Database { get; } = new();
    public FakeEventProvider Provider { get; } = new();
    public FakePublisher Publisher { get; } = new();
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));

    public DateTime Now => Clock.GetUtcNow().UtcDateTime;

    public TestHarness()
    {
        using var db = Database.CreateContext();
        db.GuildConfigurations.Add(new GuildConfiguration
        {
            GuildId = GuildId,
            EventChannelId = ChannelId,
            Country = "US",
            Regions = ["Texas"],
            Enabled = true,
            LookAheadDays = 30,
        });
        db.SaveChanges();
    }

    public async Task<SyncResult> SyncAsync()
    {
        await using var db = Database.CreateContext();
        var rsvps = new RsvpService(db, Clock, NullLogger<RsvpService>.Instance);
        var sync = new EventSyncService(db, Provider, Publisher, rsvps, Clock, NullLogger<EventSyncService>.Instance);
        return await sync.SyncAllAsync();
    }

    public async Task<RsvpResult> RsvpAsync(ulong userId, Guid eventId, RsvpStatus status, ulong? messageId = null, ulong guildId = GuildId)
    {
        await using var db = Database.CreateContext();
        messageId ??= (await db.GuildEventMessages.SingleAsync(m => m.EventId == eventId && m.GuildId == guildId)).MessageId;
        var rsvps = new RsvpService(db, Clock, NullLogger<RsvpService>.Instance);
        return await rsvps.SetRsvpAsync(guildId, messageId.Value, userId, eventId, status);
    }

    public PokemonEvent SourceEvent(string id, DateTime? start = null, PokemonEventType type = PokemonEventType.Cup) => new()
    {
        Source = "pokedata",
        SourceEventId = id,
        Name = $"League Cup {id}",
        Game = PokemonGame.TCG,
        EventType = type,
        StartDateTime = start ?? Now.AddDays(10),
        VenueName = "Example Games",
        City = "Dallas",
        Region = "Texas",
        Country = "US",
        RegistrationUrl = $"https://www.pokemon.com/us/pokemon-trainer-club/play-pokemon-tournaments/{id}/",
        SourceUrl = "https://pokedata.ovh/events2/",
    };

    public void Dispose() => Database.Dispose();
}
