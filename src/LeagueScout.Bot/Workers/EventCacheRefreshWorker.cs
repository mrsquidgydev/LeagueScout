using Microsoft.Extensions.Options;
using LeagueScout.Application.Caching;

namespace LeagueScout.Bot.Workers;

/// <summary>
/// Keeps the shared event cache fresh. Each check works out which datasets enabled guilds need
/// (equivalent guilds share one) and requests only those that are due. Checks are local database
/// reads; PokéData is contacted at most once per dataset per refresh interval, less while backing off.
/// Wakes guild synchronization when a refresh changed events. Never posts to Discord.
/// </summary>
public sealed class EventCacheRefreshWorker(
    IServiceScopeFactory scopeFactory,
    CacheRefreshTrigger trigger,
    SyncTrigger syncTrigger,
    IOptions<EventCacheOptions> options,
    ILogger<EventCacheRefreshWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation(
            "Event cache refresh worker started; refresh interval {RefreshInterval}, check interval {CheckInterval}",
            options.Value.RefreshInterval, options.Value.CheckInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await RefreshDueDatasetsAsync(stoppingToken)) syncTrigger.Trigger();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Never let a failed check stop the bot; the next check retries.
                logger.LogError(ex, "Event cache refresh check failed");
            }

            try
            {
                await trigger.WaitAsync(options.Value.CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <returns>True when any refresh changed cached events.</returns>
    private async Task<bool> RefreshDueDatasetsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<EventDataset> datasets;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            datasets = await scope.ServiceProvider.GetRequiredService<EventCacheService>().GetRequiredDatasetsAsync(cancellationToken);
        }

        var changed = false;
        foreach (var dataset in datasets)
        {
            // A scope per dataset keeps each refresh's tracked events separate.
            await using var scope = scopeFactory.CreateAsyncScope();
            var result = await scope.ServiceProvider.GetRequiredService<EventCacheService>().EnsureFreshAsync(dataset, cancellationToken);
            changed |= result.HasChanges;
        }

        return changed;
    }
}
