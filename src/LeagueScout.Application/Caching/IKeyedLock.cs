using System.Collections.Concurrent;

namespace LeagueScout.Application.Caching;

/// <summary>
/// Serializes work per key. The in-process implementation is enough for a single instance;
/// swap in a distributed lock if LeagueScout ever runs as several instances.
/// </summary>
public interface IKeyedLock
{
    /// <summary>Waits for the key's lock. Dispose the result to release it.</summary>
    Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default);
}

public sealed class InProcessKeyedLock : IKeyedLock
{
    // One entry per dataset key; the set is small and stable, so entries are never evicted.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string key, CancellationToken cancellationToken = default)
    {
        var gate = _gates.GetOrAdd(key, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) gate.Release();
        }
    }
}
