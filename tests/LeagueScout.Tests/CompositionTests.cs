using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using LeagueScout.Application;
using LeagueScout.Application.Caching;
using LeagueScout.Application.Sync;
using LeagueScout.Domain;
using LeagueScout.Infrastructure;
using LeagueScout.Infrastructure.Persistence;

namespace LeagueScout.Tests;

public class CompositionTests
{
    [Fact]
    public async Task Upgrading_an_existing_database_keeps_guilds_events_and_posts()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();

        await migrator.MigrateAsync("20261006175819_InitialCreate");
        await db.Database.ExecuteSqlRawAsync("""
            INSERT INTO GuildConfigurations (GuildId, EventChannelId, Country, Regions, IncludeChallenges, IncludeCups, LookAheadDays, Enabled, RadiusUnit, CreatedAt, UpdatedAt)
            VALUES (111, 222, 'US', '["Texas"]', 1, 1, 30, 1, 'Miles', '2026-10-01 00:00:00', '2026-10-01 00:00:00');
            INSERT INTO Events (Id, Source, SourceEventId, Name, Game, EventType, Status, StartDateTime, Country, FirstSeenAt, LastSeenAt, LastModifiedAt)
            VALUES ('6f9619ff-8b86-d011-b42d-00c04fc964ff', 'pokedata', '26-10-000001', 'Cup', 'TCG', 'Cup', 'Active', '2026-10-20 00:00:00', 'US', '2026-10-01 00:00:00', '2026-10-01 00:00:00', '2026-10-01 00:00:00');
            INSERT INTO GuildEventMessages (Id, GuildId, EventId, ChannelId, MessageId, CreatedAt, LastUpdatedAt)
            VALUES ('7f9619ff-8b86-d011-b42d-00c04fc964ff', 111, '6f9619ff-8b86-d011-b42d-00c04fc964ff', 222, 999, '2026-10-01 00:00:00', '2026-10-01 00:00:00');
            """);

        await migrator.MigrateAsync();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var guild = await db.GuildConfigurations.SingleAsync();
        Assert.Equal(["Texas"], guild.Regions);
        Assert.Null(guild.LastSyncedAt);
        var pokemonEvent = await db.Events.SingleAsync();
        Assert.Equal(0, pokemonEvent.MissingCount);
        Assert.Null(pokemonEvent.MissingSince);
        Assert.Equal(999UL, (await db.GuildEventMessages.SingleAsync()).MessageId);
        Assert.Empty(await db.DatasetSyncStates.ToListAsync());
    }

    [Fact]
    public void Services_resolve_with_default_configuration()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection()
            .AddLogging()
            .AddApplication()
            .AddInfrastructure(configuration, ":memory:")
            .AddSingleton<Application.Publishing.IEventMessagePublisher, FakePublisher>();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<EventCacheService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<EventSyncService>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<EventCacheDiagnosticsService>());

        var options = provider.GetRequiredService<IOptions<EventCacheOptions>>().Value;
        Assert.Equal(TimeSpan.FromHours(6), options.RefreshInterval);
        Assert.Equal(2, options.MissingEventThreshold);
    }

    [Fact]
    public void Invalid_cache_options_are_rejected()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["PokeData:RefreshInterval"] = "00:01:00" })
            .Build();
        using var provider = new ServiceCollection()
            .AddLogging()
            .AddApplication()
            .AddInfrastructure(configuration, ":memory:")
            .BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<EventCacheOptions>>().Value);
    }
}
