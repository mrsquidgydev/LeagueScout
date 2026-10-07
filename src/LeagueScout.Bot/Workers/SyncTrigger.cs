namespace LeagueScout.Bot.Workers;

/// <summary>Lets other components wake a worker now instead of waiting for its next interval.</summary>
public abstract class WorkerTrigger
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Trigger()
    {
        try
        {
            _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // A run is already queued.
        }
    }

    /// <summary>Waits until triggered or until <paramref name="timeout"/> elapses.</summary>
    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _signal.WaitAsync(timeout, cancellationToken);
}

/// <summary>Wakes <see cref="EventSyncWorker"/>: match cached events to guilds and post to Discord.</summary>
public sealed class SyncTrigger : WorkerTrigger;

/// <summary>Wakes <see cref="EventCacheRefreshWorker"/>: refresh any dataset that is due. Never forces a request.</summary>
public sealed class CacheRefreshTrigger : WorkerTrigger;
