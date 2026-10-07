namespace LeagueScout.Domain;

/// <summary>
/// Per-Discord-server event filtering and posting settings.
/// Location is either Country + Regions or Latitude + Longitude + Radius. Radius wins when both are set.
/// </summary>
public class GuildConfiguration
{
    public ulong GuildId { get; set; }
    public ulong? EventChannelId { get; set; }

    /// <summary>ISO 3166-1 alpha-2 country code, e.g. "US".</summary>
    public string? Country { get; set; }

    /// <summary>Region names as used by the source, e.g. "Texas".</summary>
    public List<string> Regions { get; set; } = [];

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public double? Radius { get; set; }
    public DistanceUnit RadiusUnit { get; set; } = DistanceUnit.Miles;

    public bool IncludeChallenges { get; set; } = true;
    public bool IncludeCups { get; set; } = true;

    public int LookAheadDays { get; set; } = 30;

    public bool Enabled { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>Last time cached events were matched and posted for this guild.</summary>
    public DateTime? LastSyncedAt { get; set; }

    public bool HasRadiusFilter => Latitude is not null && Longitude is not null && Radius is > 0;

    public bool HasRegionFilter => !string.IsNullOrWhiteSpace(Country);

    /// <summary>Returns null when the configuration can drive a sync, otherwise the reason it cannot.</summary>
    public string? GetSyncBlocker()
    {
        if (!Enabled) return "Event bot is disabled.";
        if (EventChannelId is null) return "No event channel configured.";
        if (!HasRadiusFilter && !HasRegionFilter) return "No location configured (country/region or latitude/longitude/radius).";
        if (!IncludeChallenges && !IncludeCups) return "Both League Challenges and League Cups are excluded.";
        if (LookAheadDays <= 0) return "Look-ahead days must be positive.";
        return null;
    }
}
