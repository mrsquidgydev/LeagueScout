namespace LeagueScout.Domain;

/// <summary>
/// Refresh bookkeeping for one upstream dataset, e.g. every PokéData event in a country.
/// Persisted so restarts keep cache freshness and failure backoff. One per (Source, DatasetKey).
/// </summary>
public class DatasetSyncState
{
    public required string Source { get; set; }

    /// <summary>Normalized description of the upstream request, e.g. "country:US". Never a guild ID.</summary>
    public required string DatasetKey { get; set; }

    public DateTime? LastAttemptAt { get; set; }
    public DateTime? LastSuccessAt { get; set; }
    public DateTime? LastFailureAt { get; set; }

    /// <summary>Short, log-safe failure description. No stack traces.</summary>
    public string? LastFailureReason { get; set; }

    /// <summary>HTTP status of the last failed request, when there was one.</summary>
    public int? LastFailureStatusCode { get; set; }

    public int ConsecutiveFailures { get; set; }

    /// <summary>Earliest time the next upstream request may be made: the refresh interval after a success, or the backoff after a failure.</summary>
    public DateTime NextAllowedRequestAt { get; set; }

    /// <summary>Events returned by the last successful request.</summary>
    public int LastResultCount { get; set; }

    public bool IsDue(DateTime utcNow) => utcNow >= NextAllowedRequestAt;

    public TimeSpan? CacheAge(DateTime utcNow) => utcNow - LastSuccessAt;

    /// <summary>
    /// Records that a request is about to be made. <paramref name="provisionalNextAllowed"/> applies if the
    /// process dies before the outcome is recorded, so a crash loop cannot hammer the source.
    /// </summary>
    public void RecordAttempt(DateTime utcNow, DateTime provisionalNextAllowed)
    {
        LastAttemptAt = utcNow;
        NextAllowedRequestAt = provisionalNextAllowed;
    }

    public void RecordSuccess(DateTime utcNow, int resultCount, DateTime nextAllowed)
    {
        LastSuccessAt = utcNow;
        LastResultCount = resultCount;
        ConsecutiveFailures = 0;
        NextAllowedRequestAt = nextAllowed;
    }

    public void RecordFailure(DateTime utcNow, string reason, int? statusCode, DateTime nextAllowed)
    {
        LastFailureAt = utcNow;
        LastFailureReason = reason.Length > MaxReasonLength ? reason[..MaxReasonLength] : reason;
        LastFailureStatusCode = statusCode;
        ConsecutiveFailures++;
        NextAllowedRequestAt = nextAllowed;
    }

    public const int MaxReasonLength = 512;
}
