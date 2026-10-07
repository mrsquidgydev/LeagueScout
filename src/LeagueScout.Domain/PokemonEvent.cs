namespace LeagueScout.Domain;

/// <summary>
/// A normalized Pokémon event, independent of the provider it came from.
/// All date/time values are UTC.
/// </summary>
public class PokemonEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public required string Source { get; set; }
    public required string SourceEventId { get; set; }

    public required string Name { get; set; }
    public PokemonGame Game { get; set; }
    public PokemonEventType EventType { get; set; }
    public EventStatus Status { get; set; } = EventStatus.Active;

    public DateTime StartDateTime { get; set; }
    public DateTime? EndDateTime { get; set; }

    /// <summary>IANA time zone of the venue, when known.</summary>
    public string? TimeZoneId { get; set; }

    public string? VenueName { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? Region { get; set; }
    public string? PostalCode { get; set; }
    public string? Country { get; set; }

    public double? Latitude { get; set; }
    public double? Longitude { get; set; }

    public string? RegistrationUrl { get; set; }
    public string? SourceUrl { get; set; }

    public DateTime FirstSeenAt { get; set; }
    public DateTime LastSeenAt { get; set; }

    /// <summary>Last time a material change was detected.</summary>
    public DateTime LastModifiedAt { get; set; }

    /// <summary>
    /// Successful source refreshes in a row that should have listed this upcoming event but did not.
    /// Failed refreshes never count. Reset when the event reappears.
    /// </summary>
    public int MissingCount { get; set; }

    /// <summary>First successful refresh in the current run of <see cref="MissingCount"/>.</summary>
    public DateTime? MissingSince { get; set; }

    public bool HasStarted(DateTime utcNow) => StartDateTime <= utcNow;

    /// <summary>
    /// Copies source data onto this event and reports what changed.
    /// Identity, status and tracking timestamps are not copied.
    /// </summary>
    public EventChangeSet ApplySourceData(PokemonEvent source)
    {
        var changes = new EventChangeSet();

        Set(changes, nameof(StartDateTime), StartDateTime, source.StartDateTime, v => StartDateTime = v, material: true);
        Set(changes, nameof(EndDateTime), EndDateTime, source.EndDateTime, v => EndDateTime = v, material: true);
        Set(changes, nameof(EventType), EventType, source.EventType, v => EventType = v, material: true);
        Set(changes, nameof(VenueName), VenueName, source.VenueName, v => VenueName = v, material: true);
        Set(changes, nameof(Address), Address, source.Address, v => Address = v, material: true);
        Set(changes, nameof(City), City, source.City, v => City = v, material: true);
        Set(changes, nameof(Region), Region, source.Region, v => Region = v, material: true);
        Set(changes, nameof(RegistrationUrl), RegistrationUrl, source.RegistrationUrl, v => RegistrationUrl = v, material: true);

        Set(changes, nameof(Name), Name, source.Name, v => Name = v, material: false);
        Set(changes, nameof(Game), Game, source.Game, v => Game = v, material: false);
        Set(changes, nameof(TimeZoneId), TimeZoneId, source.TimeZoneId, v => TimeZoneId = v, material: false);
        Set(changes, nameof(PostalCode), PostalCode, source.PostalCode, v => PostalCode = v, material: false);
        Set(changes, nameof(Country), Country, source.Country, v => Country = v, material: false);
        Set(changes, nameof(Latitude), Latitude, source.Latitude, v => Latitude = v, material: false);
        Set(changes, nameof(Longitude), Longitude, source.Longitude, v => Longitude = v, material: false);
        Set(changes, nameof(SourceUrl), SourceUrl, source.SourceUrl, v => SourceUrl = v, material: false);

        return changes;
    }

    /// <summary>Marks the event as seen in the source. Clears missing state and reactivates a removed event.</summary>
    public EventChangeSet MarkSeen(DateTime utcNow)
    {
        var changes = new EventChangeSet();
        LastSeenAt = utcNow;
        MissingCount = 0;
        MissingSince = null;
        Set(changes, nameof(Status), Status, EventStatus.Active, v => Status = v, material: true);
        return changes;
    }

    /// <summary>
    /// Records one successful source refresh that did not list the event.
    /// Marks it removed once <paramref name="threshold"/> consecutive misses are reached.
    /// </summary>
    public EventChangeSet RecordMissing(DateTime utcNow, int threshold)
    {
        MissingCount++;
        MissingSince ??= utcNow;
        return MissingCount >= threshold ? MarkRemoved() : new EventChangeSet();
    }

    /// <summary>Marks the event as no longer listed by the source.</summary>
    public EventChangeSet MarkRemoved()
    {
        var changes = new EventChangeSet();
        Set(changes, nameof(Status), Status, EventStatus.Removed, v => Status = v, material: true);
        return changes;
    }

    private static void Set<T>(EventChangeSet changes, string field, T current, T incoming, Action<T> assign, bool material)
    {
        if (EqualityComparer<T>.Default.Equals(current, incoming))
        {
            return;
        }

        changes.Add(new EventFieldChange(field, current?.ToString(), incoming?.ToString(), material));
        assign(incoming);
    }
}

public sealed record EventFieldChange(string Field, string? OldValue, string? NewValue, bool IsMaterial);

public sealed class EventChangeSet
{
    private readonly List<EventFieldChange> _changes = [];

    public IReadOnlyList<EventFieldChange> Changes => _changes;
    public bool HasChanges => _changes.Count > 0;
    public bool IsMaterial => _changes.Any(c => c.IsMaterial);

    public void Add(EventFieldChange change) => _changes.Add(change);

    public void Merge(EventChangeSet other) => _changes.AddRange(other._changes);

    public override string ToString() =>
        string.Join("; ", _changes.Select(c => $"{c.Field}: '{c.OldValue}' -> '{c.NewValue}'"));
}
