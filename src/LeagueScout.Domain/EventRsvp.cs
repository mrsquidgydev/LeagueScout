namespace LeagueScout.Domain;

/// <summary>A user's attendance intent for an event within one guild. One per (EventId, GuildId, DiscordUserId).</summary>
public class EventRsvp
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public Guid EventId { get; set; }
    public PokemonEvent Event { get; set; } = null!;

    public ulong GuildId { get; set; }
    public ulong DiscordUserId { get; set; }

    public RsvpStatus Status { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
