using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using LeagueScout.Application.Providers;
using LeagueScout.Application.Queries;
using LeagueScout.Bot.Discord;
using LeagueScout.Domain;
using LeagueScout.Infrastructure.Geocoding;

namespace LeagueScout.Tests;

public class NearbySearchTests
{
    private static readonly GeocodedLocation Austin = new(30.2711, -97.7437, "Austin, Travis County, Texas, United States");

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero));
    private readonly FakeGeocoder _geocoder = new();
    private readonly FakeEventProvider _provider = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());

    private DateTime Now => _clock.GetUtcNow().UtcDateTime;

    private NearbyEventSearchService CreateService() => new(_geocoder, _provider, _cache, _clock);

    private PokemonEvent Event(string id, DateTime start, double? lat = 30.2711, double? lng = -97.7437) => new()
    {
        Source = "pokedata",
        SourceEventId = id,
        Name = $"League Cup {id}",
        Game = PokemonGame.TCG,
        EventType = PokemonEventType.Cup,
        StartDateTime = start,
        VenueName = "Example Games",
        City = "Austin",
        Region = "Texas",
        Latitude = lat,
        Longitude = lng,
        RegistrationUrl = $"https://www.pokemon.com/us/pokemon-trainer-club/play-pokemon-tournaments/{id}/",
    };

    [Fact]
    public async Task Unknown_place_returns_null_without_calling_provider()
    {
        var result = await CreateService().SearchAsync("Nowhere", 50, DistanceUnit.Miles, 30);

        Assert.Null(result);
        Assert.Equal(0, _provider.Calls);
    }

    [Fact]
    public async Task Search_uses_radius_criteria_and_returns_upcoming_events_soonest_first()
    {
        _geocoder.Places["austin, tx"] = Austin;
        _provider.Events =
        [
            Event("late", Now.AddDays(5)),
            Event("started", Now.AddHours(-1)),
            Event("soon", Now.AddDays(1), lat: 30.5, lng: -97.7437),
        ];

        var result = await CreateService().SearchAsync("  Austin, TX ", 25, DistanceUnit.Kilometers, 14);

        Assert.NotNull(result);
        Assert.Equal(Austin, result.Location);
        Assert.Equal(["soon", "late"], result.Events.Select(e => e.Event.SourceEventId));

        var criteria = Assert.Single(_provider.Criteria);
        Assert.True(criteria.UsesRadius);
        Assert.Equal(30.27, criteria.Latitude);
        Assert.Equal(-97.74, criteria.Longitude);
        Assert.Equal(25, criteria.Radius);
        Assert.Equal(DistanceUnit.Kilometers, criteria.RadiusUnit);
        Assert.Equal(new DateOnly(2026, 9, 30), criteria.StartDate);
        Assert.Equal(new DateOnly(2026, 10, 15), criteria.EndDate);
        Assert.Equal([PokemonEventType.Challenge, PokemonEventType.Cup], criteria.EventTypes);

        // 0.2289° of latitude is about 25.5 km.
        Assert.InRange(result.Events[0].Distance!.Value, 25, 26);
        Assert.InRange(result.Events[1].Distance!.Value, 0, 0.01);
    }

    [Fact]
    public async Task Repeat_searches_are_served_from_cache()
    {
        _geocoder.Places["austin, tx"] = Austin;
        _provider.Events = [Event("a", Now.AddDays(1))];
        var service = CreateService();

        await service.SearchAsync("Austin, TX", 50, DistanceUnit.Miles, 30);
        await service.SearchAsync("austin, tx", 50, DistanceUnit.Miles, 30);
        await service.SearchAsync("Nowhere", 50, DistanceUnit.Miles, 30);
        await service.SearchAsync("Nowhere", 50, DistanceUnit.Miles, 30);

        Assert.Equal(2, _geocoder.Calls);
        Assert.Equal(1, _provider.Calls);

        // A different radius is a different PokéData query.
        await service.SearchAsync("Austin, TX", 25, DistanceUnit.Miles, 30);
        Assert.Equal(2, _provider.Calls);
    }

    [Fact]
    public void Distance_is_null_without_event_coordinates()
    {
        Assert.Null(NearbyEventSearchService.Distance(Austin, Event("x", Now, lat: null, lng: null), DistanceUnit.Miles));
    }

    [Fact]
    public void Cooldown_blocks_until_it_expires()
    {
        var cooldown = new UserCooldown(_cache, _clock);

        Assert.True(cooldown.TryStart("near", 1, TimeSpan.FromSeconds(30), out _));
        Assert.False(cooldown.TryStart("near", 1, TimeSpan.FromSeconds(30), out var remaining));
        Assert.Equal(TimeSpan.FromSeconds(30), remaining);
        Assert.True(cooldown.TryStart("near", 2, TimeSpan.FromSeconds(30), out _));

        _clock.Advance(TimeSpan.FromSeconds(31));
        Assert.True(cooldown.TryStart("near", 1, TimeSpan.FromSeconds(30), out _));
    }

    [Fact]
    public void Nearby_embed_shows_resolved_place_and_events()
    {
        var result = new NearbyEventSearchResult(Austin, [new NearbyEvent(Event("26-10-1", Now.AddDays(1)), 12.4)]);

        var embed = EventEmbedBuilder.BuildNearbyList(result, 50, DistanceUnit.Miles, 30, limit: 15);

        Assert.Contains("Austin, Travis County, Texas, United States", embed.Description);
        Assert.Contains("within 50 mi, next 30 days", embed.Description);
        Assert.Contains("[Example Games League Cup](https://www.pokemon.com/us/pokemon-trainer-club/play-pokemon-tournaments/26-10-1/)", embed.Description);
        Assert.Contains("· 12 mi", embed.Description);
        Assert.Contains("OpenStreetMap", embed.Footer!.Value.Text);
    }

    [Fact]
    public void Nearby_embed_reports_hidden_events()
    {
        var events = Enumerable.Range(0, 20).Select(i => new NearbyEvent(Event($"e{i}", Now.AddDays(1 + i)), 1)).ToList();

        var embed = EventEmbedBuilder.BuildNearbyList(new NearbyEventSearchResult(Austin, events), 50, DistanceUnit.Miles, 30, limit: 15);

        Assert.Contains("…and 5 more.", embed.Description);
    }

    [Fact]
    public async Task Nominatim_response_maps_to_location()
    {
        const string json = """[{"lat":"30.2711286","lon":"-97.7436995","display_name":"Austin, Travis County, Texas, United States"}]""";
        var handler = new RecordingHandler(HttpStatusCode.OK, json);
        using var geocoder = CreateGeocoder(handler);

        var location = await geocoder.GeocodeAsync("Austin, TX");

        Assert.Equal(new GeocodedLocation(30.2711286, -97.7436995, "Austin, Travis County, Texas, United States"), location);
        Assert.Equal("https://nominatim.openstreetmap.org/search?format=jsonv2&limit=1&q=Austin%2C%20TX", handler.LastRequestUri?.AbsoluteUri);
    }

    [Fact]
    public async Task Nominatim_empty_result_returns_null()
    {
        using var geocoder = CreateGeocoder(new RecordingHandler(HttpStatusCode.OK, "[]"));

        Assert.Null(await geocoder.GeocodeAsync("Nowhere"));
    }

    [Fact]
    public async Task Nominatim_failure_throws_geocoding_exception()
    {
        using var geocoder = CreateGeocoder(new RecordingHandler(HttpStatusCode.ServiceUnavailable, ""));

        await Assert.ThrowsAsync<GeocodingException>(() => geocoder.GeocodeAsync("Austin, TX"));
    }

    private static NominatimGeocoder CreateGeocoder(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://nominatim.openstreetmap.org/") };
        return new NominatimGeocoder(new StubHttpClientFactory(client), Options.Create(new NominatimOptions()), TimeProvider.System);
    }

    private sealed class FakeGeocoder : IGeocoder
    {
        public Dictionary<string, GeocodedLocation> Places { get; } = [];
        public int Calls { get; private set; }

        public Task<GeocodedLocation?> GeocodeAsync(string query, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(Places.GetValueOrDefault(query.ToLowerInvariant()));
        }
    }

    private sealed class RecordingHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? LastRequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
        }
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
