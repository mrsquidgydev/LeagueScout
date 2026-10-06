using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using LeagueScout.Application.Persistence;
using LeagueScout.Application.Providers;
using LeagueScout.Infrastructure.PokeData;
using LeagueScout.Infrastructure.Persistence;
using LeagueScout.Infrastructure.TimeZones;

namespace LeagueScout.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, string databasePath)
    {
        services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
        services.AddScoped<IApplicationDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.Configure<PokeDataOptions>(configuration.GetSection(PokeDataOptions.SectionName));
        services.AddSingleton<ITimeZoneResolver, GeoTimeZoneResolver>();

        var pokeData = configuration.GetSection(PokeDataOptions.SectionName).Get<PokeDataOptions>() ?? new PokeDataOptions();

        services
            .AddHttpClient(PokeDataEventProvider.HttpClientName, (sp, client) =>
            {
                var options = sp.GetRequiredService<IOptions<PokeDataOptions>>().Value;
                client.BaseAddress = options.BaseAddress;
                client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
                client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
                // Same header the Events v2 page sends.
                client.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");
                // Resilience handler owns timeouts.
                client.Timeout = Timeout.InfiniteTimeSpan;
            })
            .AddStandardResilienceHandler(options =>
            {
                options.Retry.MaxRetryAttempts = pokeData.MaxRetryAttempts;
                options.Retry.Delay = TimeSpan.FromSeconds(2);
                options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
                options.AttemptTimeout.Timeout = pokeData.AttemptTimeout;
                options.TotalRequestTimeout.Timeout = pokeData.TotalTimeout;
                options.CircuitBreaker.SamplingDuration = pokeData.AttemptTimeout * 2;
            });

        services.AddScoped<IEventProvider, PokeDataEventProvider>();

        return services;
    }
}
