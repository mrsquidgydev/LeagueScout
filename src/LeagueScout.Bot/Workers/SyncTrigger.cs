namespace LeagueScout.Bot.Workers;

/// <summary>Lets other components request an immediate sync instead of waiting for the next interval.</summary>
public sealed class SyncTrigger
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
            // A sync is already queued.
        }
    }

    /// <summary>Waits until triggered or until <paramref name="timeout"/> elapses.</summary>
    public Task WaitAsync(TimeSpan timeout, CancellationToken cancellationToken) =>
        _signal.WaitAsync(timeout, cancellationToken);
}
