using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using LeagueScout.Application.Providers;

namespace LeagueScout.Infrastructure.Geocoding;

/// <summary>
/// Geocodes free text with Nominatim. Requests are serialized and spaced by
/// <see cref="NominatimOptions.MinRequestInterval"/> across all callers, so register as a singleton.
/// </summary>
public sealed class NominatimGeocoder(IHttpClientFactory httpClientFactory, IOptions<NominatimOptions> options, TimeProvider clock)
    : IGeocoder, IDisposable
{
    public const string HttpClientName = "nominatim";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public async Task<GeocodedLocation?> GeocodeAsync(string query, CancellationToken cancellationToken = default)
    {
        List<NominatimPlaceDto>? places;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var wait = _lastRequestAt + options.Value.MinRequestInterval - clock.GetUtcNow();
            if (wait > TimeSpan.Zero) await Task.Delay(wait, clock, cancellationToken);

            try
            {
                var http = httpClientFactory.CreateClient(HttpClientName);
                using var response = await http.GetAsync(BuildRequestUri(query), cancellationToken);
                response.EnsureSuccessStatusCode();
                places = await response.Content.ReadFromJsonAsync<List<NominatimPlaceDto>>(cancellationToken);
            }
            catch (Exception ex) when ((ex is HttpRequestException or JsonException or TaskCanceledException)
                                       && !cancellationToken.IsCancellationRequested)
            {
                throw new GeocodingException($"Nominatim request failed: {ex.Message}", ex);
            }
            finally
            {
                _lastRequestAt = clock.GetUtcNow();
            }
        }
        finally
        {
            _gate.Release();
        }

        var place = places?.FirstOrDefault();
        if (place is null
            || !double.TryParse(place.Lat, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
            || !double.TryParse(place.Lon, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
        {
            return null;
        }

        var name = string.IsNullOrWhiteSpace(place.DisplayName) ? query : place.DisplayName;
        return new GeocodedLocation(latitude, longitude, name);
    }

    internal static string BuildRequestUri(string query) =>
        $"search?format=jsonv2&limit=1&q={Uri.EscapeDataString(query)}";

    public void Dispose() => _gate.Dispose();

    /// <summary>One element of the <c>/search?format=jsonv2</c> response. Coordinates are strings.</summary>
    internal sealed class NominatimPlaceDto
    {
        [JsonPropertyName("lat")] public string? Lat { get; set; }
        [JsonPropertyName("lon")] public string? Lon { get; set; }
        [JsonPropertyName("display_name")] public string? DisplayName { get; set; }
    }
}
