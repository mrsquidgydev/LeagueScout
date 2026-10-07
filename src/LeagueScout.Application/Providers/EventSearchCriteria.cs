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
}
