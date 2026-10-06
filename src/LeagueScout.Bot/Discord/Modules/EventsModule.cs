using System.Text;
using Discord;
using Discord.Interactions;
using LeagueScout.Application.Queries;
using LeagueScout.Domain;

namespace LeagueScout.Bot.Discord.Modules;

[Group("events", "Pokémon events in this server")]
[CommandContextType(InteractionContextType.Guild)]
public sealed class EventsModule(EventQueryService queries) : InteractionModuleBase<SocketInteractionContext>
{
    private const int ListLimit = 15;

    [SlashCommand("upcoming", "Show upcoming events the bot knows about")]
    public async Task UpcomingAsync()
    {
        var events = await queries.GetUpcomingAsync(Context.Guild.Id, ListLimit);
        if (events.Count == 0)
        {
            await RespondAsync("No upcoming events yet.", ephemeral: true);
            return;
        }

        await RespondAsync(embed: BuildList("Upcoming events", events), ephemeral: true);
    }

    [SlashCommand("mine", "Show events you're interested in or going to")]
    public async Task MineAsync()
    {
        var events = await queries.GetUserEventsAsync(Context.Guild.Id, Context.User.Id, ListLimit);
        if (events.Count == 0)
        {
            await RespondAsync("You haven't marked any upcoming events as Interested or Going.", ephemeral: true);
            return;
        }

        await RespondAsync(embed: BuildList("Your events", events), ephemeral: true);
    }

    private static Embed BuildList(string title, IReadOnlyList<GuildEventListing> events)
    {
        var lines = new StringBuilder();
        foreach (var (ev, message, userStatus) in events)
        {
            var status = userStatus switch
            {
                RsvpStatus.Going => "✅ ",
                RsvpStatus.Interested => "👀 ",
                _ => "",
            };
            var place = ev.City is null ? "" : $" · {Format.Sanitize(ev.City)}";
            lines.AppendLine(
                $"{status}{EventEmbedBuilder.Timestamp(ev.StartDateTime, 'f')} — " +
                $"[{Format.Sanitize(EventEmbedBuilder.ShortName(ev))}]({EventEmbedBuilder.MessageLink(message)}){place}");
        }

        return new EmbedBuilder()
            .WithTitle(title)
            .WithDescription(lines.ToString())
            .WithFooter(EventEmbedBuilder.SourceDisclaimer)
            .Build();
    }
}
