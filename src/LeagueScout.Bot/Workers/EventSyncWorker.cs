using LeagueScout.Application.Sync;
using LeagueScout.Bot.Discord;

namespace LeagueScout.Bot.Workers;

/// <summary>Runs event synchronization on a fixed interval, or sooner when triggered.</summary>
public sealed class EventSyncWorker(
    IServiceScopeFactory scopeFactory,
    DiscordReadySignal discordReady,
    SyncTrigger trigger,
    BotOptions options,
    ILogger<EventSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Waiting for Discord before the first sync; interval {SyncInterval}", options.EventSyncInterval);
        await discordReady.WaitAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var sync = scope.ServiceProvider.GetRequiredService<EventSyncService>();
                await sync.SyncAllAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // Never let a failed run stop the bot; the next interval retries.
                logger.LogError(ex, "Event synchronization failed");
            }

            try
            {
                await trigger.WaitAsync(options.EventSyncInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }
}
