using LeagueScout.Application.Rsvps;
using LeagueScout.Bot.Discord;
using LeagueScout.Domain;

namespace LeagueScout.Tests;

public class DiscordRenderingTests
{
    private static PokemonEvent Event(EventStatus status = EventStatus.Active) => new()
    {
        Source = "pokedata",
        SourceEventId = "26-10-001183",
        Name = "Wirth Collecting League Cup Q3 2026",
        Game = PokemonGame.TCG,
        EventType = PokemonEventType.Cup,
        Status = status,
        StartDateTime = new DateTime(2026, 10, 9, 23, 0, 0, DateTimeKind.Utc),
        VenueName = "WIRTH COLLECTING",
        City = "Waxahachie",
        Region = "Texas",
        RegistrationUrl = "https://www.pokemon.com/us/pokemon-trainer-club/play-pokemon-tournaments/26-10-001183/",
        SourceUrl = "https://pokedata.ovh/events2/",
    };

    [Theory]
    [InlineData(RsvpStatus.Interested)]
    [InlineData(RsvpStatus.Going)]
    [InlineData(RsvpStatus.NotGoing)]
    public void Button_ids_round_trip(RsvpStatus status)
    {
        var eventId = Guid.NewGuid();
        var parts = RsvpButtonId.Create(eventId, status).Split(':');

        Assert.Equal(RsvpButtonId.Prefix, parts[0]);
        Assert.True(RsvpButtonId.TryParseEventId(parts[1], out var parsedId));
        Assert.True(RsvpButtonId.TryParseStatus(parts[2], out var parsedStatus));
        Assert.Equal(eventId, parsedId);
        Assert.Equal(status, parsedStatus);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000; DROP")]
    public void Malformed_event_ids_are_rejected(string token) =>
        Assert.False(RsvpButtonId.TryParseEventId(token, out _));

    [Fact]
    public void Embed_shows_details_counts_and_disclaimer()
    {
        var embed = EventEmbedBuilder.BuildEmbed(Event(), new RsvpSummary(4, 3, 1));

        Assert.Equal("League Cup — WIRTH COLLECTING", embed.Title);
        Assert.Contains("<t:1791586800:F>", embed.Description);
        Assert.Contains("WIRTH COLLECTING — Waxahachie, Texas", embed.Description);
        Assert.Contains("👀 4 Interested", embed.Description);
        Assert.Contains("✅ 3 Going", embed.Description);
        Assert.Equal(EventEmbedBuilder.SourceDisclaimer, embed.Footer?.Text);
        Assert.Equal(Event().RegistrationUrl, embed.Url);
    }

    [Fact]
    public void Removed_event_is_flagged_and_buttons_disabled()
    {
        var ev = Event(EventStatus.Removed);

        var embed = EventEmbedBuilder.BuildEmbed(ev, RsvpSummary.Empty);
        var components = EventEmbedBuilder.BuildComponents(ev);

        Assert.StartsWith("[Possibly cancelled]", embed.Title);
        Assert.Contains("may be cancelled", embed.Description);
        var buttons = components.Components.SelectMany(row => ((Discord.ActionRowComponent)row).Components)
            .Cast<Discord.ButtonComponent>().ToList();
        Assert.Equal(3, buttons.Count);
        Assert.All(buttons, b => Assert.True(b.IsDisabled));
    }
}
