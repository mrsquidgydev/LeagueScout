using Discord;
using Discord.Interactions;

namespace LeagueScout.Bot.Discord.Modules;

[CommandContextType(InteractionContextType.Guild, InteractionContextType.BotDm)]
public sealed class DonateModule : InteractionModuleBase<SocketInteractionContext>
{
    public const string KofiUrl = "https://ko-fi.com/mrsquidgy";

    [SlashCommand("donate", "Support LeagueScout's hosting costs on Ko-fi")]
    public async Task DonateAsync()
    {
        var embed = new EmbedBuilder()
            .WithTitle("Support LeagueScout")
            .WithDescription("LeagueScout is free. Donations help cover hosting and running costs. Thank you! ❤️")
            .WithUrl(KofiUrl)
            .Build();

        var buttons = new ComponentBuilder()
            .WithButton("Donate on Ko-fi", style: ButtonStyle.Link, url: KofiUrl, emote: new Emoji("☕"))
            .Build();

        await RespondAsync(embed: embed, components: buttons, ephemeral: true);
    }
}
