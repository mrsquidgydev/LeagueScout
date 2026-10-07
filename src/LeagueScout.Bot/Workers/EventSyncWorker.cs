using LeagueScout.Application.Sync;
using LeagueScout.Bot.Discord;

namespace LeagueScout.Bot.Workers;

/// <summary>
/// Matches cached events to guilds and posts them on a fixed interval, or sooner when triggered
/// (after a cache refresh changes events, or a guild is configured). Never calls PokéData.
/// </summary>
public sealed class EventSyncWorker(
    IServiceScopeFactory scopeFactory,
    DiscordReadySignal discordReady,
    SyncTrigger trigger,
    BotOptions options,
    ILogger<EventSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Waiting for Discord before the first guild sync; interval {SyncInterval}", options.EventSyncInterval);
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
                logger.LogError(ex, "Guild synchronization failed");
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
