using System.Reflection;

namespace LeagueScout.Infrastructure.PokeData;

/// <summary>
/// HTTP settings for the PokéData endpoint. Cache freshness and backoff settings share the
/// <c>PokeData</c> section; see <c>LeagueScout.Application.Caching.EventCacheOptions</c>.
/// </summary>
public sealed class PokeDataOptions
{
    public const string SectionName = "PokeData";

    public Uri BaseAddress { get; set; } = new("https://pokedata.ovh/events2/");

    /// <summary>Page linked from embeds as the data source. PokéData has no per-event page.</summary>
    public string SourceUrl { get; set; } = "https://pokedata.ovh/events2/";

    /// <summary>Identifies the bot to PokéData. Empty means <see cref="DefaultUserAgent"/>.</summary>
    public string? UserAgent { get; set; }

    /// <summary>Timeout for one HTTP attempt.</summary>
    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Timeout for one request including quick retries.</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(90);

    /// <summary>Quick retries for transient failures (network, 408, 5xx) inside one request. 429 is never retried here.</summary>
    public int MaxRetryAttempts { get; set; } = 2;

    public string EffectiveUserAgent => string.IsNullOrWhiteSpace(UserAgent) ? DefaultUserAgent : UserAgent;

    /// <summary>e.g. "LeagueScout/1.0.0 (+https://github.com/mrsquidgydev/LeagueScout)".</summary>
    public static string DefaultUserAgent { get; } = BuildDefaultUserAgent();

    private static string BuildDefaultUserAgent()
    {
        var assembly = typeof(PokeDataOptions).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? assembly.GetName().Version?.ToString(3)
                      ?? "1.0";

        // Drop source-link metadata such as "+3f0049e...".
        var plus = version.IndexOf('+');
        if (plus > 0) version = version[..plus];

        return $"LeagueScout/{version} (+https://github.com/mrsquidgydev/LeagueScout)";
    }
}
