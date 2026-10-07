using System.Text;
using Discord;
using Discord.Interactions;
using LeagueScout.Application.Providers;
using LeagueScout.Application.Queries;
using LeagueScout.Domain;

namespace LeagueScout.Bot.Discord.Modules;

[Group("events", "Pokémon events in this server")]
[CommandContextType(InteractionContextType.Guild)]
public sealed class EventsModule(EventQueryService queries, NearbyEventSearchService nearby, UserCooldown cooldown)
    : InteractionModuleBase<SocketInteractionContext>
{
    private const int ListLimit = 15;
    private static readonly TimeSpan NearCooldown = TimeSpan.FromSeconds(30);

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

    [SlashCommand("near", "Search PokéData live for League Challenges and Cups near a place")]
    public async Task NearAsync(
        [Summary("location", "City, postcode or address, e.g. Austin, TX")][MinLength(2)][MaxLength(100)] string location,
        [Summary("radius", "Search radius (default 50)")][MinValue(1)][MaxValue(500)] double radius = 50,
        [Summary("unit", "Radius unit (default miles)")] DistanceUnit unit = DistanceUnit.Miles,
        [Summary("days", "How many days ahead to look (default 30)")][MinValue(1)][MaxValue(120)] int days = 30)
    {
        if (!cooldown.TryStart("events-near", Context.User.Id, NearCooldown, out var remaining))
        {
            await RespondAsync($"Please wait {Math.Ceiling(remaining.TotalSeconds):0}s before searching again.", ephemeral: true);
            return;
        }

        // Geocoding plus PokéData can exceed Discord's 3-second response window.
        await DeferAsync(ephemeral: true);

        NearbyEventSearchResult? result;
        try
        {
            result = await nearby.SearchAsync(location, radius, unit, days);
        }
        catch (GeocodingException)
        {
            await FollowupAsync("Location search is unavailable right now. Try again later.", ephemeral: true);
            return;
        }
        catch (EventProviderException)
        {
            await FollowupAsync("PokéData isn't responding right now. Try again later.", ephemeral: true);
            return;
        }

        if (result is null)
        {
            await FollowupAsync($"Couldn't find \"{Format.Sanitize(location)}\". Try a city and state, or a postcode.", ephemeral: true);
            return;
        }

        await FollowupAsync(embed: EventEmbedBuilder.BuildNearbyList(result, radius, unit, days, ListLimit), ephemeral: true);
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
