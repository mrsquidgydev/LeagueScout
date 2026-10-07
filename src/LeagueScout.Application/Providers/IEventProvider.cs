using LeagueScout.Domain;

namespace LeagueScout.Application.Providers;

public interface IEventProvider
{
    /// <summary>Source name stored on every event this provider returns, e.g. "pokedata".</summary>
    string SourceName { get; }

    /// <summary>
    /// Retrieves events matching the criteria as normalized, untracked domain objects.
    /// Throws <see cref="EventProviderException"/> when the source cannot be reached or parsed.
    /// </summary>
    Task<IReadOnlyCollection<PokemonEvent>> GetEventsAsync(
        EventSearchCriteria criteria,
        CancellationToken cancellationToken = default);
}

public class EventProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>HTTP status returned by the source, when it responded.</summary>
    public int? StatusCode { get; init; }

    /// <summary>How long the source asked callers to wait (HTTP <c>Retry-After</c>), when given.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>False when retrying soon is unlikely to help, e.g. a 404 or an unreadable response.</summary>
    public bool IsTransient { get; init; } = true;
}
