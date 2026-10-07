namespace LeagueScout.Application.Providers;

public sealed record GeocodedLocation(double Latitude, double Longitude, string DisplayName);

public interface IGeocoder
{
    /// <summary>
    /// Resolves free text (city, postcode or address) to coordinates. Returns null when nothing matches.
    /// Throws <see cref="GeocodingException"/> when the service cannot be reached or parsed.
    /// </summary>
    Task<GeocodedLocation?> GeocodeAsync(string query, CancellationToken cancellationToken = default);
}

public class GeocodingException(string message, Exception? innerException = null)
    : Exception(message, innerException);
