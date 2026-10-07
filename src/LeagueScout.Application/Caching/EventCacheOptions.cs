namespace LeagueScout.Application.Caching;

/// <summary>
/// Freshness, backoff and missing-event settings for the shared event cache.
/// Bound from the <c>PokeData</c> configuration section, alongside the HTTP settings.
/// </summary>
public sealed class EventCacheOptions
{
    public const string SectionName = "PokeData";

    /// <summary>How long a successful refresh stays fresh before the dataset is requested again.</summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Cache older than this is reported as stale in diagnostics. Stale data is still served.</summary>
    public TimeSpan MaxCacheAge { get; set; } = TimeSpan.FromHours(8);

    /// <summary>Wait after the first consecutive failure. Doubles per failure up to <see cref="FailureRetryMax"/>.</summary>
    public TimeSpan FailureRetryBase { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan FailureRetryMax { get; set; } = TimeSpan.FromHours(2);

    /// <summary>Consecutive successful refreshes that must omit an upcoming event before it is marked removed.</summary>
    public int MissingEventThreshold { get; set; } = 2;

    /// <summary>Random 0..N minutes added to every scheduled request so deployments do not align.</summary>
    public int JitterMinutes { get; set; } = 10;

    /// <summary>How often the refresh worker checks whether any dataset is due. Checks are local and cheap.</summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Longest <c>Retry-After</c> honoured, so a bad header cannot stop refreshes indefinitely.</summary>
    public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromHours(24);
}
