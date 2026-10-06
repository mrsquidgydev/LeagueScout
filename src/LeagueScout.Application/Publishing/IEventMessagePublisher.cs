using LeagueScout.Application.Rsvps;
using LeagueScout.Domain;

namespace LeagueScout.Application.Publishing;

/// <summary>Posts and edits event messages in a chat platform (Discord).</summary>
public interface IEventMessagePublisher
{
    /// <returns>The ID of the created message.</returns>
    Task<ulong> PostAsync(
        ulong guildId,
        ulong channelId,
        PokemonEvent pokemonEvent,
        RsvpSummary rsvps,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        GuildEventMessage message,
        PokemonEvent pokemonEvent,
        RsvpSummary rsvps,
        CancellationToken cancellationToken = default);
}
