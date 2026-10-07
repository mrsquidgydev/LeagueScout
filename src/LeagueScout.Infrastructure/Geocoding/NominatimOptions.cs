namespace LeagueScout.Infrastructure.Geocoding;

/// <summary>
/// Settings for the OpenStreetMap Nominatim search API.
/// Usage policy: https://operations.osmfoundation.org/policies/nominatim/
/// </summary>
public sealed class NominatimOptions
{
    public const string SectionName = "Nominatim";

    public Uri BaseAddress { get; set; } = new("https://nominatim.openstreetmap.org/");

    /// <summary>The usage policy requires a User-Agent that identifies the application.</summary>
    public string UserAgent { get; set; } = "LeagueScout/1.0 (+https://github.com/mrsquidgydev/LeagueScout)";

    /// <summary>Minimum gap between requests across all users. The public instance allows 1 per second.</summary>
    public TimeSpan MinRequestInterval { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromSeconds(25);
    public int MaxRetryAttempts { get; set; } = 1;
}
