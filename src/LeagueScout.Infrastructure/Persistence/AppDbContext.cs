using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using LeagueScout.Application.Persistence;
using LeagueScout.Domain;

namespace LeagueScout.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IApplicationDbContext
{
    public DbSet<PokemonEvent> Events => Set<PokemonEvent>();
    public DbSet<GuildConfiguration> GuildConfigurations => Set<GuildConfiguration>();
    public DbSet<GuildEventMessage> GuildEventMessages => Set<GuildEventMessage>();
    public DbSet<EventRsvp> EventRsvps => Set<EventRsvp>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Discord snowflakes fit in a signed 64-bit integer; PostgreSQL has no unsigned type.
        builder.Properties<ulong>().HaveConversion<long>();

        // All timestamps are UTC; make sure they come back with Kind = Utc.
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        builder.Properties<DateTime?>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PokemonEvent>(e =>
        {
            e.ToTable("Events");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Source, x.SourceEventId }).IsUnique();
            e.HasIndex(x => x.StartDateTime);

            e.Property(x => x.Source).HasMaxLength(32);
            e.Property(x => x.SourceEventId).HasMaxLength(128);
            e.Property(x => x.Name).HasMaxLength(256);
            e.Property(x => x.Game).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.EventType).HasConversion<string>().HasMaxLength(32);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.TimeZoneId).HasMaxLength(64);
            e.Property(x => x.VenueName).HasMaxLength(256);
            e.Property(x => x.Address).HasMaxLength(512);
            e.Property(x => x.City).HasMaxLength(128);
            e.Property(x => x.Region).HasMaxLength(128);
            e.Property(x => x.PostalCode).HasMaxLength(32);
            e.Property(x => x.Country).HasMaxLength(8);
            e.Property(x => x.RegistrationUrl).HasMaxLength(512);
            e.Property(x => x.SourceUrl).HasMaxLength(512);
        });

        modelBuilder.Entity<GuildConfiguration>(e =>
        {
            e.ToTable("GuildConfigurations");
            e.HasKey(x => x.GuildId);
            e.Property(x => x.GuildId).ValueGeneratedNever();
            e.Property(x => x.Country).HasMaxLength(8);
            e.Property(x => x.RadiusUnit).HasConversion<string>().HasMaxLength(16);
            e.Ignore(x => x.HasRadiusFilter);
            e.Ignore(x => x.HasRegionFilter);
        });

        modelBuilder.Entity<GuildEventMessage>(e =>
        {
            e.ToTable("GuildEventMessages");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.GuildId, x.EventId }).IsUnique();
            e.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EventRsvp>(e =>
        {
            e.ToTable("EventRsvps");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.GuildId, x.EventId, x.DiscordUserId }).IsUnique();
            e.HasIndex(x => new { x.GuildId, x.DiscordUserId });
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.HasOne(x => x.Event).WithMany().HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : DateTime.SpecifyKind(v, DateTimeKind.Utc),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
