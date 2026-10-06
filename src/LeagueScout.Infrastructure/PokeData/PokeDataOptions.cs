namespace LeagueScout.Infrastructure.PokeData;

public sealed class PokeDataOptions
{
    public const string SectionName = "PokeData";

    public Uri BaseAddress { get; set; } = new("https://pokedata.ovh/events2/");

    /// <summary>Page linked from embeds as the data source. PokéData has no per-event page.</summary>
    public string SourceUrl { get; set; } = "https://pokedata.ovh/events2/";

    public string UserAgent { get; set; } = "LeagueScout/1.0";

    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(90);
    public int MaxRetryAttempts { get; set; } = 2;
}
