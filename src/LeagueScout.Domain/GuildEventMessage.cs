namespace LeagueScout.Domain;

/// <summary>The Discord message posted for an event in a guild.</summary>
public class GuildEventMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public ulong GuildId { get; set; }
    public Guid EventId { get; set; }
    public PokemonEvent Event { get; set; } = null!;

    public ulong ChannelId { get; set; }
    public ulong MessageId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime LastUpdatedAt { get; set; }
}
