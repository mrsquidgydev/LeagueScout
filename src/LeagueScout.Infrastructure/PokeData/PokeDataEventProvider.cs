using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using LeagueScout.Application.Providers;
using LeagueScout.Domain;
using LeagueScout.Infrastructure.TimeZones;

namespace LeagueScout.Infrastructure.PokeData;

/// <summary>
/// Reads events from the JSON endpoint used by the PokéData Events v2 page.
/// See docs/pokedata-investigation.md for the verified interface.
/// </summary>
public sealed class PokeDataEventProvider : IEventProvider
{
    public const string HttpClientName = "pokedata";

    private const string EventsPath = "events.php";

    private readonly HttpClient _http;
    private readonly PokeDataEventMapper _mapper;
    private readonly TimeProvider _clock;
    private readonly ILogger<PokeDataEventProvider> _logger;

    public PokeDataEventProvider(
        IHttpClientFactory httpClientFactory,
        ITimeZoneResolver timeZoneResolver,
        IOptions<PokeDataOptions> options,
        TimeProvider clock,
        ILogger<PokeDataEventProvider> logger)
    {
        _http = httpClientFactory.CreateClient(HttpClientName);
        _mapper = new PokeDataEventMapper(timeZoneResolver, options.Value.SourceUrl);
        _clock = clock;
        _logger = logger;
    }

    public string SourceName => PokeDataEventMapper.SourceName;

    /// <remarks>
    /// PokéData sends no ETag or Last-Modified header, so requests are never conditional.
    /// Transient failures are retried briefly by the HTTP resilience handler (never 429); longer
    /// backoff is the caller's job, using <see cref="EventProviderException.RetryAfter"/> when present.
    /// </remarks>
    public async Task<IReadOnlyCollection<PokemonEvent>> GetEventsAsync(
        EventSearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        var requestUri = BuildRequestUri(criteria);
        var startedAt = _clock.GetTimestamp();

        List<PokeDataEventDto>? dtos;
        try
        {
            using var response = await _http.GetAsync(requestUri, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                var status = (int)response.StatusCode;
                throw new EventProviderException($"PokéData returned HTTP {status}.")
                {
                    StatusCode = status,
                    RetryAfter = GetRetryAfter(response),
                    IsTransient = IsTransientStatus(status),
                };
            }

            dtos = await response.Content.ReadFromJsonAsync<List<PokeDataEventDto>>(cancellationToken);
        }
        catch (JsonException ex)
        {
            // The page or format changed; retrying soon will not help.
            throw new EventProviderException($"PokéData response could not be read: {ex.Message}", ex) { IsTransient = false };
        }
        catch (Exception ex) when ((ex is HttpRequestException or TaskCanceledException or Polly.ExecutionRejectedException)
                                   && !cancellationToken.IsCancellationRequested)
        {
            // Network errors, timeouts and an open circuit breaker.
            throw new EventProviderException($"PokéData request failed: {ex.GetType().Name}: {ex.Message}", ex);
        }

        _logger.LogDebug(
            "PokéData returned {RecordCount} records in {DurationMs} ms for {RequestUri}",
            dtos?.Count ?? 0, (long)_clock.GetElapsedTime(startedAt).TotalMilliseconds, requestUri);

        return MapAll(dtos ?? [], criteria);
    }

    private TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        var header = response.Headers.RetryAfter;
        if (header?.Delta is { } delta) return delta;
        if (header?.Date is { } date)
        {
            var wait = date - _clock.GetUtcNow();
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }

        return null;
    }

    private static bool IsTransientStatus(int status) => status is 408 or 429 || status >= 500;

    internal IReadOnlyCollection<PokemonEvent> MapAll(IEnumerable<PokeDataEventDto> dtos, EventSearchCriteria criteria)
    {
        var events = new List<PokemonEvent>();
        foreach (var dto in dtos)
        {
            var mapped = _mapper.Map(dto, out var skipReason);
            if (mapped is null)
            {
                _logger.LogWarning("Skipping PokéData record {PokeDataId}: {Reason}", dto.Id, skipReason);
                continue;
            }

            // Defensive: never pass through records outside what was asked for.
            if (mapped.Game != criteria.Game || !criteria.EventTypes.Contains(mapped.EventType)) continue;

            events.Add(mapped);
        }

        return events;
    }

    internal static string BuildRequestUri(EventSearchCriteria criteria)
    {
        var types = string.Join(',', criteria.EventTypes.Select(PokeDataEventMapper.ToQueryValue));

        // Mirror the parameter set the Events v2 page sends; empty values mean "no filter".
        var query = new Dictionary<string, string>
        {
            ["includePast"] = "false",
            ["leagueMode"] = "false",
            ["country"] = criteria.UsesRadius ? "" : criteria.Country ?? "",
            ["stateCodes"] = criteria.UsesRadius ? "" : string.Join(',', criteria.Regions),
            ["city"] = "",
            ["shop"] = "",
            ["spatialMode"] = "radius",
            ["polygonPoints"] = "",
            ["lat"] = criteria.UsesRadius ? Format(criteria.Latitude!.Value) : "",
            ["lng"] = criteria.UsesRadius ? Format(criteria.Longitude!.Value) : "",
            ["radius"] = criteria.UsesRadius ? Format(criteria.Radius!.Value) : "",
            ["unit"] = criteria.RadiusUnit == DistanceUnit.Kilometers ? "km" : "mi",
            ["startDate"] = criteria.StartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["endDate"] = criteria.EndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["tcgTypes"] = criteria.Game == PokemonGame.TCG ? types : "",
            ["vgTypes"] = criteria.Game == PokemonGame.VG ? types : "",
            ["goTypes"] = criteria.Game == PokemonGame.GO ? types : "",
        };

        return EventsPath + "?" + string.Join('&', query.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}"));
    }

    private static string Format(double value) => value.ToString("0.#####", CultureInfo.InvariantCulture);
}
