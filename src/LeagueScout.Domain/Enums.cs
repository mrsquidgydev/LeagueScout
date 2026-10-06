namespace LeagueScout.Domain;

public enum PokemonGame
{
    TCG,
    VG,
    GO,
}

public enum PokemonEventType
{
    Challenge,
    Cup,
    Regional,
    International,
    SpecialEvent,
    Prerelease,
    Friendly,
    Other,
}

public enum EventStatus
{
    Active,

    /// <summary>
    /// The event no longer appears in the source. It may have been cancelled.
    /// </summary>
    Removed,
}

public enum RsvpStatus
{
    Interested,
    Going,
    NotGoing,
}

public enum DistanceUnit
{
    Miles,
    Kilometers,
}
