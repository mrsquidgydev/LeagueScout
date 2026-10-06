using LeagueScout.Domain;

namespace LeagueScout.Application.Providers;

public sealed record EventSearchCriteria
{
    public PokemonGame Game { get; init; } = PokemonGame.TCG;
    public required IReadOnlyCollection<PokemonEventType> EventTypes { get; init; }

    public string? Country { get; init; }
    public IReadOnlyCollection<string> Regions { get; init; } = [];

    public double? Latitude { get; init; }
    public double? Longitude { get; init; }
    public double? Radius { get; init; }
    public DistanceUnit RadiusUnit { get; init; } = DistanceUnit.Miles;

    /// <summary>Inclusive start date. Dates are interpreted by the source (venue-local).</summary>
    public required DateOnly StartDate { get; init; }

    /// <summary>Inclusive end date.</summary>
    public required DateOnly EndDate { get; init; }

    public bool UsesRadius => Latitude is not null && Longitude is not null && Radius is > 0;

    public static EventSearchCriteria ForGuild(GuildConfiguration guild, DateTime utcNow)
    {
        var types = new List<PokemonEventType>();
        if (guild.IncludeChallenges) types.Add(PokemonEventType.Challenge);
        if (guild.IncludeCups) types.Add(PokemonEventType.Cup);

        // Start a day early: the source filters on venue-local dates, which may lag UTC.
        var today = DateOnly.FromDateTime(utcNow);

        var criteria = new EventSearchCriteria
        {
            Game = PokemonGame.TCG,
            EventTypes = types,
            StartDate = today.AddDays(-1),
            EndDate = today.AddDays(guild.LookAheadDays),
        };

        return guild.HasRadiusFilter
            ? criteria with
            {
                Latitude = guild.Latitude,
                Longitude = guild.Longitude,
                Radius = guild.Radius,
                RadiusUnit = guild.RadiusUnit,
            }
            : criteria with
            {
                Country = guild.Country,
                Regions = guild.Regions,
            };
    }
}
