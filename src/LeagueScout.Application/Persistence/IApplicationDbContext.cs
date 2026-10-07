using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using LeagueScout.Domain;

namespace LeagueScout.Application.Persistence;

/// <summary>
/// Provider-agnostic persistence surface. The database provider (SQLite, PostgreSQL) lives in Infrastructure.
/// </summary>
public interface IApplicationDbContext
{
    DbSet<PokemonEvent> Events { get; }
    DbSet<GuildConfiguration> GuildConfigurations { get; }
    DbSet<GuildEventMessage> GuildEventMessages { get; }
    DbSet<EventRsvp> EventRsvps { get; }
    DbSet<DatasetSyncState> DatasetSyncStates { get; }

    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
