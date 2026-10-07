using Microsoft.Extensions.DependencyInjection;
using LeagueScout.Application.Caching;
using LeagueScout.Application.Guilds;
using LeagueScout.Application.Queries;
using LeagueScout.Application.Rsvps;
using LeagueScout.Application.Sync;

namespace LeagueScout.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddMemoryCache();

        services.AddOptions<EventCacheOptions>()
            .Validate(o => o.RefreshInterval >= TimeSpan.FromMinutes(15), "PokeData:RefreshInterval must be at least 00:15:00.")
            .Validate(o => o.FailureRetryBase > TimeSpan.Zero && o.FailureRetryMax >= o.FailureRetryBase,
                "PokeData:FailureRetryBase must be positive and no greater than PokeData:FailureRetryMax.")
            .Validate(o => o.MissingEventThreshold >= 1, "PokeData:MissingEventThreshold must be at least 1.")
            .Validate(o => o.JitterMinutes >= 0, "PokeData:JitterMinutes must not be negative.")
            .Validate(o => o.CheckInterval >= TimeSpan.FromSeconds(10), "PokeData:CheckInterval must be at least 00:00:10.")
            .ValidateOnStart();
        services.AddSingleton<IKeyedLock, InProcessKeyedLock>();
        services.AddSingleton<EventCacheStatistics>();
        services.AddScoped<EventCacheService>();
        services.AddScoped<CachedEventReader>();
        services.AddScoped<EventCacheDiagnosticsService>();

        services.AddScoped<EventSyncService>();
        services.AddScoped<RsvpService>();
        services.AddScoped<EventQueryService>();
        services.AddScoped<NearbyEventSearchService>();
        services.AddScoped<GuildConfigurationService>();
        return services;
    }
}
