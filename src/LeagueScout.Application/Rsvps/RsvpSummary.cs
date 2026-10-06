namespace LeagueScout.Application.Rsvps;

public sealed record RsvpSummary(int Interested, int Going, int NotGoing)
{
    public static readonly RsvpSummary Empty = new(0, 0, 0);
}
