using System.Globalization;

namespace LeagueScout.Bot;

/// <summary>Deployment settings read from environment variables.</summary>
public sealed class BotOptions
{
    public required string DiscordToken { get; init; }
    public required string DatabasePath { get; init; }
    public required TimeSpan EventSyncInterval { get; init; }

    /// <summary>When set, slash commands register to this guild only (instant; for development).</summary>
    public ulong? CommandGuildId { get; init; }

    public static BotOptions FromConfiguration(IConfiguration configuration)
    {
        var token = configuration["DISCORD_TOKEN"];
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("DISCORD_TOKEN is not set.");
        }

        var intervalText = configuration["EVENT_SYNC_INTERVAL"];
        var interval = TimeSpan.FromHours(6);
        if (!string.IsNullOrWhiteSpace(intervalText)
            && !TimeSpan.TryParse(intervalText, CultureInfo.InvariantCulture, out interval))
        {
            throw new InvalidOperationException($"EVENT_SYNC_INTERVAL '{intervalText}' is not a valid TimeSpan (e.g. 06:00:00).");
        }

        if (interval < TimeSpan.FromMinutes(15))
        {
            throw new InvalidOperationException("EVENT_SYNC_INTERVAL must be at least 00:15:00 to avoid over-polling PokéData.");
        }

        ulong? commandGuildId = null;
        var guildText = configuration["DISCORD_COMMAND_GUILD_ID"];
        if (!string.IsNullOrWhiteSpace(guildText))
        {
            commandGuildId = ulong.TryParse(guildText, out var id)
                ? id
                : throw new InvalidOperationException($"DISCORD_COMMAND_GUILD_ID '{guildText}' is not a valid ID.");
        }

        var databasePath = configuration["DATABASE_PATH"];

        return new BotOptions
        {
            DiscordToken = token,
            DatabasePath = string.IsNullOrWhiteSpace(databasePath) ? "data/leaguescout.db" : databasePath,
            EventSyncInterval = interval,
            CommandGuildId = commandGuildId,
        };
    }
}
