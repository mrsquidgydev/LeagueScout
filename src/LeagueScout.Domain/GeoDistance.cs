namespace LeagueScout.Domain;

/// <summary>Great-circle distance helpers for radius filtering.</summary>
public static class GeoDistance
{
    public static double Between(double fromLatitude, double fromLongitude, double toLatitude, double toLongitude, DistanceUnit unit)
    {
        var earthRadius = EarthRadius(unit);
        var dLat = ToRadians(toLatitude - fromLatitude);
        var dLng = ToRadians(toLongitude - fromLongitude);
        var a = Math.Pow(Math.Sin(dLat / 2), 2)
                + Math.Cos(ToRadians(fromLatitude)) * Math.Cos(ToRadians(toLatitude)) * Math.Pow(Math.Sin(dLng / 2), 2);
        return earthRadius * 2 * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>
    /// A latitude/longitude box that contains every point within <paramref name="radius"/>.
    /// Used to narrow database queries before the exact distance check. Longitude bounds are null
    /// when the box would cross a pole or the antimeridian.
    /// </summary>
    public static GeoBox BoundingBox(double latitude, double longitude, double radius, DistanceUnit unit)
    {
        var latDelta = radius / EarthRadius(unit) * 180 / Math.PI;
        var minLat = latitude - latDelta;
        var maxLat = latitude + latDelta;

        if (minLat <= -90 || maxLat >= 90)
        {
            return new GeoBox(Math.Max(minLat, -90), Math.Min(maxLat, 90), null, null);
        }

        var lngDelta = latDelta / Math.Cos(ToRadians(Math.Max(Math.Abs(minLat), Math.Abs(maxLat))));
        var minLng = longitude - lngDelta;
        var maxLng = longitude + lngDelta;

        return minLng < -180 || maxLng > 180
            ? new GeoBox(minLat, maxLat, null, null)
            : new GeoBox(minLat, maxLat, minLng, maxLng);
    }

    private static double EarthRadius(DistanceUnit unit) => unit == DistanceUnit.Kilometers ? 6371.0 : 3958.8;

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}

public readonly record struct GeoBox(double MinLatitude, double MaxLatitude, double? MinLongitude, double? MaxLongitude);
