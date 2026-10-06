using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using LeagueScout.Application.Providers;
using LeagueScout.Domain;
using LeagueScout.Infrastructure.PokeData;
using LeagueScout.Infrastructure.TimeZones;

namespace LeagueScout.Tests;

public class PokeDataProviderTests
{
    private static readonly string FixtureJson =
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "pokedata-events.json"));

    private static readonly EventSearchCriteria TcgChallengesAndCups = new()
    {
        EventTypes = [PokemonEventType.Challenge, PokemonEventType.Cup],
        Country = "US",
        Regions = ["Texas"],
        StartDate = new DateOnly(2026, 10, 1),
        EndDate = new DateOnly(2026, 10, 31),
    };

    private static PokeDataEventProvider CreateProvider(HttpMessageHandler handler)
    {
        var factory = new StubHttpClientFactory(new HttpClient(handler) { BaseAddress = new Uri("https://pokedata.ovh/events2/") });
        return new PokeDataEventProvider(
            factory, new GeoTimeZoneResolver(), Options.Create(new PokeDataOptions()), NullLogger<PokeDataEventProvider>.Instance);
    }

    [Fact]
    public async Task Fixture_maps_to_normalized_domain_events()
    {
        var provider = CreateProvider(new StubHandler(HttpStatusCode.OK, FixtureJson));

        var events = (await provider.GetEventsAsync(TcgChallengesAndCups)).ToList();

        // The "friendly" record is outside the requested types.
        Assert.Equal(3, events.Count);

        var challenge = events.Single(e => e.SourceEventId == "26-10-014168");
        Assert.Equal("pokedata", challenge.Source);
        Assert.Equal("Cantu Collectibles TCG Challenge", challenge.Name);
        Assert.Equal(PokemonGame.TCG, challenge.Game);
        Assert.Equal(PokemonEventType.Challenge, challenge.EventType);
        Assert.Equal("CANTU COLLECTIBLES", challenge.VenueName);
        Assert.Equal("Webster", challenge.City);
        Assert.Equal("Texas", challenge.Region);
        Assert.Equal("US", challenge.Country);
        Assert.Equal(29.5459, challenge.Latitude);
        Assert.Equal(-95.1267, challenge.Longitude);
        Assert.Equal("America/Chicago", challenge.TimeZoneId);
        // 19:00 CDT (UTC-5) on Oct 6 is 00:00 UTC on Oct 7.
        Assert.Equal(new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc), challenge.StartDateTime);
        Assert.Equal(DateTimeKind.Utc, challenge.StartDateTime.Kind);
        Assert.Equal("https://www.pokemon.com/us/pokemon-trainer-club/play-pokemon-tournaments/26-10-014168/", challenge.RegistrationUrl);
        Assert.Equal("https://pokedata.ovh/events2/", challenge.SourceUrl);

        var cup = events.Single(e => e.SourceEventId == "26-10-001183");
        Assert.Equal(PokemonEventType.Cup, cup.EventType);
        Assert.Equal(new DateTime(2026, 10, 9, 23, 0, 0, DateTimeKind.Utc), cup.StartDateTime);
    }

    [Fact]
    public async Task Records_without_tournament_id_or_valid_date_are_skipped()
    {
        const string json = """
            [
              {"id":"a","title":"No id","when":"2026-10-06 19:00:00","game":"tcg","type":"cups","lat":29.5,"lng":-95.1},
              {"id":"b","title":"Bad date","when":"soon","pokemon_url":"26-10-1","game":"tcg","type":"cups"},
              {"id":"c","title":"Good","when":"2026-10-06 19:00:00","pokemon_url":"26-10-2","game":"tcg","type":"cups","lat":29.5,"lng":-95.1}
            ]
            """;
        var provider = CreateProvider(new StubHandler(HttpStatusCode.OK, json));

        var events = await provider.GetEventsAsync(TcgChallengesAndCups);

        Assert.Equal("26-10-2", Assert.Single(events).SourceEventId);
    }

    [Fact]
    public async Task Http_failure_throws_provider_exception()
    {
        var provider = CreateProvider(new StubHandler(HttpStatusCode.ServiceUnavailable, "down"));

        await Assert.ThrowsAsync<EventProviderException>(() => provider.GetEventsAsync(TcgChallengesAndCups));
    }

    [Fact]
    public async Task Malformed_json_throws_provider_exception()
    {
        var provider = CreateProvider(new StubHandler(HttpStatusCode.OK, "<html>maintenance</html>"));

        var ex = await Assert.ThrowsAsync<EventProviderException>(() => provider.GetEventsAsync(TcgChallengesAndCups));
        Assert.IsAssignableFrom<JsonException>(ex.InnerException);
    }

    [Fact]
    public void Region_search_builds_verified_query()
    {
        var uri = PokeDataEventProvider.BuildRequestUri(TcgChallengesAndCups);

        Assert.StartsWith("events.php?", uri);
        Assert.Contains("country=US", uri);
        Assert.Contains("stateCodes=Texas", uri);
        Assert.Contains("tcgTypes=challenges%2Ccups", uri);
        Assert.Contains("vgTypes=&", uri);
        Assert.Contains("startDate=2026-10-01", uri);
        Assert.Contains("endDate=2026-10-31", uri);
        Assert.Contains("includePast=false", uri);
        Assert.Contains("lat=&", uri);
    }

    [Fact]
    public void Radius_search_builds_verified_query_and_ignores_region()
    {
        var criteria = TcgChallengesAndCups with
        {
            Latitude = 32.7767,
            Longitude = -96.797,
            Radius = 50,
            RadiusUnit = DistanceUnit.Miles,
        };

        var uri = PokeDataEventProvider.BuildRequestUri(criteria);

        Assert.Contains("spatialMode=radius", uri);
        Assert.Contains("lat=32.7767", uri);
        Assert.Contains("lng=-96.797", uri);
        Assert.Contains("radius=50", uri);
        Assert.Contains("unit=mi", uri);
        Assert.Contains("country=&", uri);
        Assert.Contains("stateCodes=&", uri);
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
            });
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }
}
