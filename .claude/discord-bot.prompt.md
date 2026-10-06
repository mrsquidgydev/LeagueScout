# Pokémon Premier Event Discord Bot

I want to build a Discord bot for a local competitive Pokémon community.

The bot's purpose is to discover nearby Pokémon TCG Premier Events—primarily League Challenges and League Cups—and post them into a configured Discord channel so members can indicate whether they are interested in or attending the event.

The initial event-data source is:

https://pokedata.ovh/events2/

## Product Goal

Help players in a local Pokémon community discover Premier Events and coordinate attendance.

This should not simply be an event-notification bot.

The important functionality is:

PokéData event discovery → Discord event post → community RSVP tracking.

A successful experience should look roughly like:

**League Cup — Example Games**

📅 Saturday, October 17 — 11:00 AM  
📍 Example Games — Dallas, TX  
🏆 Pokémon TCG League Cup

👀 4 Interested  
✅ 3 Going

[👀 Interested] [✅ Going] [❌ Not Going]

Users should be able to change their RSVP later.

Only one RSVP state may exist for a user/event at a time.

---

# Phase 0 — PokéData Investigation

Before implementing the integration, investigate how:

https://pokedata.ovh/events2/

retrieves event information.

Do NOT assume or invent an API endpoint.

Use browser developer tools or other appropriate inspection methods to identify the network calls performed when:

1. TCG is selected.
2. Cups are selected.
3. Challenges are selected.
4. A US state is selected.
5. A geographic radius search is performed.
6. A date range is supplied.
7. CSV export is requested.

Document:

- request URL
- HTTP method
- parameters
- response format
- event identifiers
- event schema
- pagination, if applicable
- rate limiting, if identifiable
- whether the endpoint appears intended for public consumption
- whether event records have stable unique IDs

Do not bypass authentication, access controls, or other restrictions.

Preferred integration order:

1. Documented/public PokéData API
2. Stable endpoint legitimately used by Events v2
3. CSV export
4. HTML parsing only as a last resort

Before implementing `PokeDataEventProvider`, summarize the findings.

---

# Technology

Use:

- .NET 10
- C#
- .NET Worker Service
- Discord.Net
- Entity Framework Core
- SQLite for the MVP
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging
- Microsoft.Extensions.Configuration
- HttpClientFactory

Structure the project so SQLite can later be replaced by PostgreSQL without changing the domain or application layers.

Do not build a web frontend.

Do not introduce Kubernetes, Redis, message queues, microservices, or other infrastructure unless there is a demonstrated requirement.

The MVP should be deployable as a single Docker container.

---

# Architecture

Use clear separation between:

- Domain
- Application services
- Infrastructure
- Discord integration
- Event provider integrations

Suggested project structure:

src/
  LeagueScout.Bot/
  LeagueScout.Application/
  LeagueScout.Domain/
  LeagueScout.Infrastructure/

tests/
  LeagueScout.Tests/

The exact structure may change if there is a cleaner conventional .NET architecture, but avoid unnecessary abstraction.

---

# Event Provider Abstraction

Create an abstraction conceptually similar to:

```csharp
public interface IEventProvider
{
    Task<IReadOnlyCollection<PokemonEvent>> GetEventsAsync(
        EventSearchCriteria criteria,
        CancellationToken cancellationToken = default);
}
```

Implement:

```text
PokeDataEventProvider
```

PokéData DTOs must remain isolated inside the PokéData infrastructure integration.

Do not expose PokéData DTOs to the Discord layer.

Convert source records into normalized domain objects.

---

# Normalized Event Model

The normalized `PokemonEvent` should support data resembling:

```text
Id
Source
SourceEventId

Name
Game
EventType

StartDateTime
EndDateTime

VenueName
Address
City
Region
PostalCode
Country

Latitude
Longitude

RegistrationUrl
SourceUrl

FirstSeenAt
LastSeenAt
LastModifiedAt
```

The exact types should be chosen appropriately.

Potential enums:

```text
PokemonGame
- TCG
- VG
- GO

PokemonEventType
- Challenge
- Cup
- Regional
- International
- SpecialEvent
- Prerelease
- Friendly
- Other
```

The MVP only needs:

```text
Game = TCG

EventType =
- Challenge
- Cup
```

but the domain model should not prevent additional types later.

---

# Event Identity and Deduplication

Prefer a stable event identifier supplied by PokéData.

If one exists, store:

```text
Source = "pokedata"
SourceEventId = <identifier>
```

and enforce uniqueness on:

```text
(Source, SourceEventId)
```

If PokéData does not provide stable IDs, develop a deterministic fingerprint from stable event properties.

Do not use Discord message IDs as event identity.

Events should be upserted.

An event may be:

- newly discovered
- unchanged
- updated
- cancelled/removed
- expired

Do not assume event information is immutable.

---

# Discord Posting

A newly discovered event matching the Guild configuration should generate one Discord message.

Store the relationship:

```text
GuildEventMessage

GuildId
EventId
ChannelId
MessageId
CreatedAt
LastUpdatedAt
```

This prevents the polling process from creating duplicate messages.

Use Discord embeds.

An event embed should contain, when available:

- event type
- venue
- event date/time
- city/state
- registration URL
- source URL
- RSVP summary

Example:

```text
🏆 League Cup

Example Games
Dallas, TX

📅 Saturday, October 17
🕚 11:00 AM

👀 Interested: 4
✅ Going: 3

[👀 Interested] [✅ Going] [❌ Not Going]
```

Use Discord's timestamp syntax where practical so users see dates/times in their own timezone.

Include a small indication that event information comes from PokéData and should be verified with the tournament organizer.

---

# RSVP System

Use Discord buttons rather than message reactions.

Supported statuses:

```text
Interested
Going
NotGoing
```

No RSVP represents a user who has not expressed interest.

Do not add `NotInterested` unless a concrete use case appears later.

Model:

```text
EventRsvp

EventId
GuildId
DiscordUserId
Status
CreatedAt
UpdatedAt
```

Enforce one RSVP record per:

```text
EventId + GuildId + DiscordUserId
```

Pressing a button changes the existing RSVP.

For example:

```text
Interested → Going
Going → NotGoing
NotGoing → Interested
```

The interaction response should be ephemeral, for example:

```text
You're marked as Going for Example Games League Cup.
```

After an RSVP changes, update the original embed's aggregate counts.

Do not send a new channel message for RSVP changes.

---

# Discord Component IDs

Discord component custom IDs must contain enough information to identify the action without exposing or trusting arbitrary user-provided data.

Conceptually:

```text
event-rsvp:{eventId}:interested
event-rsvp:{eventId}:going
event-rsvp:{eventId}:not-going
```

Validate every interaction server-side.

Do not trust IDs or state received from Discord without verifying the associated event and guild.

---

# Guild Configuration

The bot should eventually support more than one Discord server even if initially deployed to one.

Create a `GuildConfiguration` model.

Potential fields:

```text
GuildId
EventChannelId

Country
Regions

Latitude
Longitude
Radius
RadiusUnit

IncludeChallenges
IncludeCups

LookAheadDays

Enabled
```

Do not require every filtering mechanism simultaneously.

For the MVP, support either:

```text
Country + Region
```

or:

```text
Latitude + Longitude + Radius
```

depending on what PokéData supports most reliably.

Configuration must not be hard-coded into the event provider.

---

# Slash Commands

Implement a small command surface.

## /events upcoming

Shows upcoming events currently known to the bot.

Optional parameters could eventually include:

```text
type
distance
date
```

Do not overbuild these filters initially.

## /events mine

Shows events where the requesting user is:

```text
Interested
Going
```

## /eventbot status

Displays the server's current event-bot configuration.

Administrative configuration commands may be added once the core event pipeline works.

---

# Event Synchronization

Create an event synchronization background service.

Conceptually:

```text
EventSyncWorker
    ↓
IEventProvider
    ↓
Normalize
    ↓
Compare / Upsert
    ↓
Database
    ↓
DiscordEventPublisher
```

Polling interval should be configurable.

A reasonable initial interval would be several hours rather than aggressively polling PokéData.

Do not unnecessarily query PokéData every few minutes.

The synchronization process must be idempotent.

Running the same synchronization repeatedly with unchanged source data must not generate duplicate Discord messages.

---

# Handling Event Updates

If an existing event changes materially, update the Discord embed.

Potential material changes:

- date
- time
- venue
- address
- event type
- registration URL
- cancellation

Avoid noisy Discord messages for insignificant source changes.

Log detected changes.

If an event appears cancelled, clearly indicate that status in the existing message rather than deleting historical information.

---

# Error Handling

Failure to reach PokéData must not crash the Discord bot.

Use:

- HttpClientFactory
- sensible timeout
- limited retry/backoff
- structured logging
- cancellation tokens

Do not retry indefinitely.

If the provider is unavailable, log the failure and allow the next scheduled synchronization to try again.

---

# Database

Use EF Core migrations.

Suggested entities:

```text
PokemonEvent
GuildConfiguration
GuildEventMessage
EventRsvp
```

Add appropriate unique indexes.

Especially:

```text
PokemonEvent(Source, SourceEventId)

GuildEventMessage(GuildId, EventId)

EventRsvp(GuildId, EventId, DiscordUserId)
```

Use UTC internally.

---

# Testing

Unit test the behaviors most likely to create bad Discord UX.

At minimum:

1. Same source event returned twice does not create duplicate events.
2. Same synchronization run twice does not create duplicate Discord messages.
3. RSVP Interested → Going updates rather than inserts another RSVP.
4. Different users may RSVP independently.
5. Event updates modify the existing event.
6. Provider failure does not corrupt existing data.
7. PokéData DTO parsing correctly maps to normalized domain events.

Use test fixtures containing representative PokéData responses once the actual response format has been discovered.

---

# Observability

Use structured logging.

Important log events:

```text
Event synchronization started
Events retrieved
New events discovered
Events updated
Discord event message created
Discord event message updated
RSVP updated
Provider request failed
Synchronization completed
```

Do not log Discord bot tokens or other credentials.

---

# Configuration and Secrets

Use environment variables for deployment.

Example:

```text
DISCORD_TOKEN
DATABASE_PATH
EVENT_SYNC_INTERVAL
```

Guild-specific event filtering belongs in the database, not environment variables.

Provide:

```text
.env.example
```

but never commit real secrets.

---

# Docker

Provide a production-capable Dockerfile using a multi-stage .NET build.

The container should:

1. start the Discord bot
2. initialize/migrate the database appropriately
3. start the synchronization worker

For SQLite deployments, persist the database using a mounted volume.

---

# MVP Definition

The first usable release is complete when:

1. The bot connects to Discord.
2. PokéData event retrieval works.
3. TCG Challenges and Cups can be filtered for the configured local area.
4. Events are persisted.
5. New events are posted once to a configured Discord channel.
6. Users can select Interested, Going, or Not Going.
7. Users can change their RSVP.
8. RSVP totals update on the Discord embed.
9. Repeated polling does not duplicate event messages.
10. Changes to known events update the existing Discord post.
11. The application runs in Docker.

Do not add unrelated features until this workflow functions end-to-end.

---

# Future Features — Do Not Implement Yet

Keep the architecture compatible with these ideas, but do not implement them as part of the MVP:

- configurable reminder notifications
- "who's going?" command
- carpool coordination
- tournament-result tracking
- Championship Point tracking
- Regional/Special Event support
- registration-opening notifications
- calendar export
- Google Calendar integration
- user travel radius preferences
- event discussion threads
- automatic Discord scheduled events
- multiple event providers
- web administration dashboard
- analytics
- leaderboards

Avoid speculative abstractions solely for these future features.

---

# Implementation Process

Work incrementally.

## Step 1
Investigate PokéData Events v2 and document its actual data interface.

Do not write an imagined API integration.

## Step 2
Create the domain model and `IEventProvider`.

## Step 3
Build `PokeDataEventProvider` using the verified interface.

## Step 4
Persist events with EF Core.

## Step 5
Implement Discord event embeds.

## Step 6
Implement RSVP buttons and persistence.

## Step 7
Implement idempotent scheduled synchronization.

## Step 8
Implement event-update handling.

## Step 9
Add slash commands.

## Step 10
Add tests, Docker support, configuration documentation, and deployment instructions.

At the end of each step:

- summarize what was implemented
- list files created/modified
- list architectural decisions
- identify unresolved questions
- run relevant tests/builds
- do not proceed based on assumptions when source behavior can instead be verified