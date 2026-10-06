using Microsoft.Extensions.DependencyInjection;
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
        services.AddScoped<EventSyncService>();
        services.AddScoped<RsvpService>();
        services.AddScoped<EventQueryService>();
        services.AddScoped<GuildConfigurationService>();
        return services;
    }
}
