using LeagueScout.Domain;

namespace LeagueScout.Bot.Discord;

/// <summary>Builds and parses RSVP button custom IDs: <c>event-rsvp:{eventId}:{status}</c>.</summary>
public static class RsvpButtonId
{
    public const string Prefix = "event-rsvp";

    public static string Create(Guid eventId, RsvpStatus status) => $"{Prefix}:{eventId:N}:{ToToken(status)}";

    public static string ToToken(RsvpStatus status) => status switch
    {
        RsvpStatus.Interested => "interested",
        RsvpStatus.Going => "going",
        RsvpStatus.NotGoing => "not-going",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static bool TryParseStatus(string token, out RsvpStatus status)
    {
        switch (token)
        {
            case "interested": status = RsvpStatus.Interested; return true;
            case "going": status = RsvpStatus.Going; return true;
            case "not-going": status = RsvpStatus.NotGoing; return true;
            default: status = default; return false;
        }
    }

    public static bool TryParseEventId(string token, out Guid eventId) =>
        Guid.TryParseExact(token, "N", out eventId);
}
