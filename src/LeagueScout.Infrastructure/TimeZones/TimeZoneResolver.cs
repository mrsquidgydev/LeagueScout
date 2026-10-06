using System.Collections.Concurrent;
using GeoTimeZone;

namespace LeagueScout.Infrastructure.TimeZones;

public interface ITimeZoneResolver
{
    /// <summary>Returns the time zone at the coordinates, or null when it cannot be determined.</summary>
    TimeZoneInfo? Resolve(double latitude, double longitude);
}

/// <summary>Offline coordinate-to-IANA-zone lookup.</summary>
public sealed class GeoTimeZoneResolver : ITimeZoneResolver
{
    private readonly ConcurrentDictionary<string, TimeZoneInfo?> _zones = new();

    public TimeZoneInfo? Resolve(double latitude, double longitude)
    {
        var ianaId = TimeZoneLookup.GetTimeZone(latitude, longitude).Result;
        if (string.IsNullOrEmpty(ianaId)) return null;

        return _zones.GetOrAdd(ianaId, static id =>
            TimeZoneInfo.TryFindSystemTimeZoneById(id, out var zone) ? zone : null);
    }
}
