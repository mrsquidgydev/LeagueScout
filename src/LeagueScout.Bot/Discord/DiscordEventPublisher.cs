using Discord;
using Discord.WebSocket;
using LeagueScout.Application.Publishing;
using LeagueScout.Application.Rsvps;
using LeagueScout.Domain;

namespace LeagueScout.Bot.Discord;

public sealed class DiscordEventPublisher(DiscordSocketClient client, DiscordReadySignal ready) : IEventMessagePublisher
{
    public async Task<ulong> PostAsync(
        ulong guildId, ulong channelId, PokemonEvent pokemonEvent, RsvpSummary rsvps, CancellationToken cancellationToken = default)
    {
        var channel = await GetChannelAsync(guildId, channelId, cancellationToken);

        var message = await channel.SendMessageAsync(
            embed: EventEmbedBuilder.BuildEmbed(pokemonEvent, rsvps),
            components: EventEmbedBuilder.BuildComponents(pokemonEvent),
            allowedMentions: AllowedMentions.None,
            options: new RequestOptions { CancelToken = cancellationToken });

        return message.Id;
    }

    public async Task UpdateAsync(
        GuildEventMessage message, PokemonEvent pokemonEvent, RsvpSummary rsvps, CancellationToken cancellationToken = default)
    {
        var channel = await GetChannelAsync(message.GuildId, message.ChannelId, cancellationToken);

        await channel.ModifyMessageAsync(message.MessageId, m =>
        {
            m.Embed = EventEmbedBuilder.BuildEmbed(pokemonEvent, rsvps);
            m.Components = EventEmbedBuilder.BuildComponents(pokemonEvent);
        }, new RequestOptions { CancelToken = cancellationToken });
    }

    private async Task<ITextChannel> GetChannelAsync(ulong guildId, ulong channelId, CancellationToken cancellationToken)
    {
        await ready.WaitAsync(cancellationToken);

        var channel = await client.GetChannelAsync(channelId, new RequestOptions { CancelToken = cancellationToken });
        if (channel is not ITextChannel text || text.GuildId != guildId)
        {
            throw new InvalidOperationException($"Channel {channelId} is not a text channel in guild {guildId}.");
        }

        return text;
    }
}
