using System.Globalization;
using System.Text;
using Discord;
using LeagueScout.Application.Queries;
using LeagueScout.Application.Rsvps;
using LeagueScout.Domain;

namespace LeagueScout.Bot.Discord;

/// <summary>Renders an event as a Discord embed with RSVP buttons.</summary>
public static class EventEmbedBuilder
{
    public const string SourceDisclaimer = "Event data from PokéData. Verify details with the tournament organizer.";

    private static readonly Color CupColor = new(0xD8, 0x1B, 0x60);
    private static readonly Color ChallengeColor = new(0x1E, 0x88, 0xE5);
    private static readonly Color OtherColor = new(0x98, 0xA1, 0xBC);
    private static readonly Color RemovedColor = new(0x60, 0x60, 0x60);

    public static Embed BuildEmbed(PokemonEvent ev, RsvpSummary rsvps)
    {
        var removed = ev.Status == EventStatus.Removed;
        var typeLabel = TypeLabel(ev.EventType);
        var title = $"{typeLabel} — {ev.VenueName ?? ev.Name}";

        var description = new StringBuilder();
        if (removed)
        {
            description.AppendLine("⚠️ **No longer listed on PokéData — this event may be cancelled. Check with the organizer.**");
            description.AppendLine();
        }

        if (!string.Equals(ev.Name, ev.VenueName, StringComparison.OrdinalIgnoreCase))
        {
            description.AppendLine($"*{Escape(ev.Name)}*");
            description.AppendLine();
        }

        description.AppendLine($"📅 {Timestamp(ev.StartDateTime, 'F')} ({Timestamp(ev.StartDateTime, 'R')})");

        var location = Location(ev);
        if (location is not null) description.AppendLine($"📍 {Escape(location)}");

        description.AppendLine($"🏆 Pokémon {GameLabel(ev.Game)} {typeLabel}");
        description.AppendLine();
        description.AppendLine($"👀 {rsvps.Interested} Interested");
        description.AppendLine($"✅ {rsvps.Going} Going");

        var links = new List<string>();
        if (ev.RegistrationUrl is not null) links.Add($"[Event page]({ev.RegistrationUrl})");
        if (ev.SourceUrl is not null) links.Add($"[PokéData]({ev.SourceUrl})");
        if (links.Count > 0)
        {
            description.AppendLine();
            description.AppendLine(string.Join(" · ", links));
        }

        var embed = new EmbedBuilder()
            .WithTitle(Truncate(removed ? $"[Possibly cancelled] {title}" : title, EmbedBuilder.MaxTitleLength))
            .WithDescription(description.ToString())
            .WithColor(removed ? RemovedColor : ColorFor(ev.EventType))
            .WithFooter(SourceDisclaimer);

        if (ev.RegistrationUrl is not null) embed.WithUrl(ev.RegistrationUrl);

        return embed.Build();
    }

    public static MessageComponent BuildComponents(PokemonEvent ev)
    {
        var disabled = ev.Status == EventStatus.Removed;

        return new ComponentBuilder()
            .WithButton("Interested", RsvpButtonId.Create(ev.Id, RsvpStatus.Interested), ButtonStyle.Secondary, new Emoji("👀"), disabled: disabled)
            .WithButton("Going", RsvpButtonId.Create(ev.Id, RsvpStatus.Going), ButtonStyle.Success, new Emoji("✅"), disabled: disabled)
            .WithButton("Not Going", RsvpButtonId.Create(ev.Id, RsvpStatus.NotGoing), ButtonStyle.Danger, new Emoji("❌"), disabled: disabled)
            .Build();
    }

    /// <summary>Renders a live nearby search. Always shows the resolved place so users can spot a wrong match.</summary>
    public static Embed BuildNearbyList(NearbyEventSearchResult result, double radius, DistanceUnit unit, int lookAheadDays, int limit)
    {
        var unitLabel = unit == DistanceUnit.Kilometers ? "km" : "mi";
        var scope = string.Create(CultureInfo.InvariantCulture, $"within {radius:0.#} {unitLabel}, next {lookAheadDays} days");

        var description = new StringBuilder();
        description.AppendLine($"📍 {Escape(result.Location.DisplayName)}");
        description.AppendLine($"*League Challenges and Cups {scope}*");
        description.AppendLine();

        if (result.Events.Count == 0)
        {
            description.AppendLine("No events found. Try a larger radius or more days.");
        }

        var shown = 0;
        foreach (var (ev, distance) in result.Events.Take(limit))
        {
            var name = Escape(ShortName(ev));
            var link = ev.RegistrationUrl is null ? $"**{name}**" : $"[{name}]({ev.RegistrationUrl})";
            var place = ev.City is null ? "" : $" · {Escape(ev.City)}";
            var away = distance is null ? "" : string.Create(CultureInfo.InvariantCulture, $" · {distance:0} {unitLabel}");
            var line = $"{Timestamp(ev.StartDateTime, 'f')} — {link}{place}{away}";

            // Leave room for the "more" line below.
            if (description.Length + line.Length > EmbedBuilder.MaxDescriptionLength - 100) break;
            description.AppendLine(line);
            shown++;
        }

        if (shown < result.Events.Count)
        {
            description.AppendLine();
            description.AppendLine($"…and {result.Events.Count - shown} more. Narrow the radius or days to see them.");
        }

        return new EmbedBuilder()
            .WithTitle("Events near you")
            .WithDescription(description.ToString())
            .WithColor(OtherColor)
            .WithFooter($"{SourceDisclaimer} Location search © OpenStreetMap contributors.")
            .Build();
    }

    /// <summary>Short one-line name, e.g. "Example Games League Cup".</summary>
    public static string ShortName(PokemonEvent ev) => $"{ev.VenueName ?? ev.Name} {TypeLabel(ev.EventType)}";

    public static string TypeLabel(PokemonEventType type) => type switch
    {
        PokemonEventType.Cup => "League Cup",
        PokemonEventType.Challenge => "League Challenge",
        PokemonEventType.Prerelease => "Prerelease",
        PokemonEventType.Friendly => "Friendly",
        PokemonEventType.Regional => "Regional Championship",
        PokemonEventType.International => "International Championship",
        PokemonEventType.SpecialEvent => "Special Event",
        _ => "Event",
    };

    public static string StatusLabel(RsvpStatus status) => status switch
    {
        RsvpStatus.Interested => "Interested",
        RsvpStatus.Going => "Going",
        RsvpStatus.NotGoing => "Not Going",
        _ => status.ToString(),
    };

    public static string Timestamp(DateTime utc, char style) =>
        $"<t:{new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds()}:{style}>";

    public static string MessageLink(GuildEventMessage message) =>
        $"https://discord.com/channels/{message.GuildId}/{message.ChannelId}/{message.MessageId}";

    private static string? Location(PokemonEvent ev)
    {
        var place = string.Join(", ", new[] { ev.City, ev.Region }.Where(s => !string.IsNullOrWhiteSpace(s)));
        return (ev.VenueName, place) switch
        {
            (null, "") => null,
            (null, _) => place,
            (_, "") => ev.VenueName,
            _ => $"{ev.VenueName} — {place}",
        };
    }

    private static string GameLabel(PokemonGame game) => game switch
    {
        PokemonGame.TCG => "TCG",
        PokemonGame.VG => "VGC",
        PokemonGame.GO => "GO",
        _ => game.ToString(),
    };

    private static Color ColorFor(PokemonEventType type) => type switch
    {
        PokemonEventType.Cup => CupColor,
        PokemonEventType.Challenge => ChallengeColor,
        _ => OtherColor,
    };

    private static string Escape(string text) => Format.Sanitize(text);

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
