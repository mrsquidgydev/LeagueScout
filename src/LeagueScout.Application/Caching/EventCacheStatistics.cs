namespace LeagueScout.Application.Caching;

public sealed record EventCacheCounters(
    long UpstreamRequests,
    long Successes,
    long Failures,
    long RateLimited,
    long ServerErrors,
    long CacheHits,
    long CacheMisses);

/// <summary>Event-source traffic counters since process start. Register as a singleton.</summary>
public sealed class EventCacheStatistics
{
    private long _upstreamRequests;
    private long _successes;
    private long _failures;
    private long _rateLimited;
    private long _serverErrors;
    private long _cacheHits;
    private long _cacheMisses;

    public void RecordCacheHit() => Interlocked.Increment(ref _cacheHits);
    public void RecordCacheMiss() => Interlocked.Increment(ref _cacheMisses);
    public void RecordRequest() => Interlocked.Increment(ref _upstreamRequests);
    public void RecordSuccess() => Interlocked.Increment(ref _successes);

    public void RecordFailure(int? statusCode)
    {
        Interlocked.Increment(ref _failures);
        if (statusCode == 429) Interlocked.Increment(ref _rateLimited);
        if (statusCode >= 500) Interlocked.Increment(ref _serverErrors);
    }

    public EventCacheCounters Snapshot() => new(
        Interlocked.Read(ref _upstreamRequests),
        Interlocked.Read(ref _successes),
        Interlocked.Read(ref _failures),
        Interlocked.Read(ref _rateLimited),
        Interlocked.Read(ref _serverErrors),
        Interlocked.Read(ref _cacheHits),
        Interlocked.Read(ref _cacheMisses));
}
