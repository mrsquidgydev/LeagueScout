using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using LeagueScout.Application.Persistence;
using LeagueScout.Domain;

namespace LeagueScout.Application.Guilds;

public class GuildConfigurationService(
    IApplicationDbContext db,
    TimeProvider clock,
    ILogger<GuildConfigurationService> logger)
{
    public Task<GuildConfiguration?> GetAsync(ulong guildId, CancellationToken cancellationToken = default) =>
        db.GuildConfigurations.SingleOrDefaultAsync(g => g.GuildId == guildId, cancellationToken);

    /// <summary>Loads the guild's configuration (or a new default one), applies <paramref name="update"/> and saves it.</summary>
    public async Task<GuildConfiguration> UpdateAsync(
        ulong guildId, Action<GuildConfiguration> update, CancellationToken cancellationToken = default)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var config = await GetAsync(guildId, cancellationToken);

        if (config is null)
        {
            config = new GuildConfiguration { GuildId = guildId, CreatedAt = now };
            db.GuildConfigurations.Add(config);
        }

        update(config);
        config.UpdatedAt = now;

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Guild configuration updated for guild {GuildId}", guildId);

        return config;
    }
}
