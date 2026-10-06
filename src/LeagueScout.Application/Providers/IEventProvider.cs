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
    : Exception(message, innerException);
