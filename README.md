# LeagueScout

Discovers nearby Pokémon TCG League Challenges and League Cups from
[PokéData Events](https://pokedata.ovh/events2/), posts each one once to a Discord channel,
and lets members RSVP with buttons (👀 Interested / ✅ Going / ❌ Not Going).

```
PokéData events.php ──► EventCacheRefreshWorker ──► shared event cache (SQLite) ──► EventSyncWorker ──► Discord embed + buttons ──► RSVPs
```

## Features (MVP)

- Polls PokéData every 6 hours by default, once per shared dataset (one per country, or per radius area), not per server.
- Filters by country + state/region, or by latitude/longitude/radius, locally against the shared cache.
- Keeps working from cached events when PokéData is down.
- Posts each new event once. Repeated polling never duplicates posts.
- Edits the existing post when the date, time, venue, location, type or link changes.
- Marks posts "Possibly cancelled" when an upcoming event is missing from 2 successful PokéData refreshes in a row. Reverts if it returns.
- One RSVP per user per event. Clicking another button changes it. Counts update on the post.
- Shows dates with Discord timestamps, so each user sees their own time zone.

## Commands

| Command | Who | What |
|---|---|---|
| `/events upcoming` | everyone | Next upcoming events posted in this server |
| `/events mine` | everyone | Upcoming events you marked Interested or Going |
| `/events near` | everyone | Live PokéData search for Challenges and Cups near a place you type, e.g. `location:Austin, TX radius:50` |
| `/eventbot status` | everyone | This server's configuration |
| `/eventbot configure` | Manage Server | Set channel, location, event types, look-ahead, enable/disable |
| `/eventbot diagnostics` | Manage Server | PokéData cache health for this server's dataset (see [Diagnostics](#diagnostics)) |
| `/donate` | everyone | Link to the Ko-fi page to support hosting costs |

All responses are ephemeral.

## Support

LeagueScout is free to use. If it helps your community, consider supporting hosting and running costs:

[![Support on Ko-fi](https://img.shields.io/badge/Ko--fi-Support%20LeagueScout-FF5E5B?logo=ko-fi&logoColor=white)](https://ko-fi.com/mrsquidgy)

<https://ko-fi.com/mrsquidgy>

Run `/donate` in Discord to get the link from the bot.

## Discord setup

1. Create an application at <https://discord.com/developers/applications>.
2. **Bot** tab: reset and copy the token. No privileged intents are needed.
3. Invite the bot. Replace `APP_ID` with the application ID:
   `https://discord.com/oauth2/authorize?client_id=APP_ID&scope=bot%20applications.commands&permissions=19456`
   (View Channels, Send Messages, Embed Links).
4. Make sure the bot can view and post in the event channel.

## Configure a server

Run in Discord as a user with **Manage Server**:

```
/eventbot configure channel:#events country:US regions:Texas enabled:True
```

Or use a radius search instead of regions:

```
/eventbot configure channel:#events latitude:32.7767 longitude:-96.797 radius:50 unit:Miles enabled:True
```

- Radius search wins when latitude, longitude and radius are all set. `radius:0` clears it.
- `regions` takes full region names as PokéData lists them (`Texas`, not `TX`), comma-separated.
- `include_challenges` / `include_cups` default to true. `lookahead_days` defaults to 30.
- Saving a valid, enabled configuration queues an immediate sync. It posts matching events already in the
  shared cache, and fetches from PokéData only if this server's dataset has never been fetched or is due.

The first sync posts every matching upcoming event in the look-ahead window. Keep the window modest.

## How PokéData is polled

PokéData request volume scales with the number of distinct datasets, not with the number of servers.

```text
                   ┌───────────────────────┐
                   │       PokéData        │
                   └───────────┬───────────┘
                               │
                 periodic refresh, per dataset
                               │
                               ▼
                   ┌────────────────────────┐
                   │ EventCacheRefreshWorker│
                   └───────────┬────────────┘
                               │ upsert
                               ▼
                   ┌───────────────────────┐
                   │ Persistent Event Cache│
                   │   SQLite / Postgres   │
                   └───────────┬───────────┘
                               │ local filtering
                 ┌─────────────┼─────────────┐
                 ▼             ▼             ▼
              Guild A       Guild B       Guild C
                 │             │             │
                 └─────────────┼─────────────┘
                               ▼
                   EventSyncWorker ──► Discord posts
```

- **Datasets.** A server's settings map to a dataset key that describes the upstream request, never the server:
  - Region servers share one dataset per country, e.g. `country:US`. PokéData returns the whole country in one
    response (about 2,200 US Challenges and Cups per month, ~0.8 MB), and regions are filtered locally.
    100 Texas servers and 50 Oklahoma servers together cause one US request per refresh interval, about 4 a day.
  - Radius servers share a dataset per centre (rounded to 0.01°) and radius, e.g. `radius:32.78,-96.80,51mi`.
  - Challenges and Cups are always fetched together. Event types and look-ahead are filtered locally; the
    dataset window is the longest look-ahead of the servers sharing it.
  - Disabled or incompletely configured servers need no dataset and cause no requests.
- **Refresh.** `EventCacheRefreshWorker` checks every `CheckInterval` (5 min, local reads only) which datasets
  are needed and requests each one only when `RefreshInterval` (6 h, plus 0–10 min jitter) has passed since its
  last success. Sync state is stored in the `DatasetSyncStates` table, so restarts do not cause extra requests.
- **Deduplication.** Refreshes for the same dataset are serialized by a keyed lock (`IKeyedLock`). A caller that
  waited re-checks freshness after acquiring it, so concurrent callers produce one request.
- **Guild sync.** `EventSyncWorker` reads only the cache. It posts new matches and edits posts whose event changed
  since they were last rendered. It runs every `EVENT_SYNC_INTERVAL`, after any refresh that changed events, and
  after `/eventbot configure`.
- **Failures.** A failed request never touches cached events. It is logged and recorded, and the next attempt
  waits 15 min, 30 min, 1 h, then 2 h (plus jitter); unreadable responses and 4xx errors wait 2 h. A `Retry-After`
  header is honoured when longer (up to 24 h). HTTP 429 is never retried immediately. Success resets the count.
- **Stale data.** Servers keep reading cached events however old they are. `MaxCacheAge` (8 h) only marks the
  cache as stale in diagnostics.
- **Missing events.** Only successful, non-empty refreshes count. An upcoming event that a refresh should have
  listed but did not is counted missing; after `MissingEventThreshold` (2) misses in a row it is marked removed and
  its posts show "Possibly cancelled". If it reappears, the count resets and the posts revert.
- **Conditional requests.** Not used: PokéData sends no `ETag` or `Last-Modified` header.
- `/events near` is the only command that queries PokéData live, because it searches arbitrary places. It is
  limited to one search per user every 30 seconds and caches results for 15 minutes.

### Diagnostics

`/eventbot diagnostics` shows this server's dataset key, health (`Healthy`, `Retrying`, `Stale`, or not yet
refreshed), last attempted and successful refresh, cache age, next allowed refresh, consecutive failures with the
last HTTP status, cached and upcoming event counts, events matching this server, last server sync, and request,
failure, 429, 5xx, cache-hit and cache-miss counters since start. It shows no secrets or exception details.

Each refresh logs one summary line:

```
pokedata refresh completed for country:US in 1712 ms: 2173 received, 4 added, 2 updated, 1 missing, 0 removed; cache age before 0.06:03:12; next refresh after 2026-10-07T22:21:40Z
```

Failures log one warning with the HTTP status, consecutive failure count and next attempt time.
Per-event changes are logged at `Debug` only.

## Environment variables

| Variable | Required | Default | Notes |
|---|---|---|---|
| `DISCORD_TOKEN` | yes | | Bot token. Never logged. |
| `DATABASE_PATH` | no | `data/leaguescout.db` (`/data/leaguescout.db` in Docker) | SQLite file; directory is created |
| `EVENT_SYNC_INTERVAL` | no | `06:00:00` | `hh:mm:ss`, minimum `00:05:00`. How often cached events are posted to servers. Does not contact PokéData. |
| `DISCORD_COMMAND_GUILD_ID` | no | | Register commands to one server (instant). Empty = global. |

PokéData settings live in `src/LeagueScout.Bot/appsettings.json` under `PokeData`. Override with env vars such
as `PokeData__RefreshInterval=12:00:00`. All have defaults; existing deployments need no new settings.

| Setting | Default | Notes |
|---|---|---|
| `PokeData:RefreshInterval` | `06:00:00` | How often each dataset is requested. Minimum `00:15:00`. Replaces the PokéData role of `EVENT_SYNC_INTERVAL`. |
| `PokeData:MaxCacheAge` | `08:00:00` | Older cache is reported as stale. Stale data is still served. |
| `PokeData:FailureRetryBase` | `00:15:00` | Wait after the first failure; doubles per consecutive failure |
| `PokeData:FailureRetryMax` | `02:00:00` | Longest failure wait (unless `Retry-After` asks for longer) |
| `PokeData:JitterMinutes` | `10` | Random 0–N minutes added to every scheduled request |
| `PokeData:MissingEventThreshold` | `2` | Successful refreshes an event must be missing from before it is marked removed |
| `PokeData:CheckInterval` | `00:05:00` | How often the refresh worker checks for due datasets (local only) |
| `PokeData:AttemptTimeout` | `00:00:30` | Timeout per HTTP attempt |
| `PokeData:TotalTimeout` | `00:01:30` | Timeout per request including quick retries |
| `PokeData:MaxRetryAttempts` | `2` | Quick retries for network errors, 408 and 5xx. Never 429. |
| `PokeData:UserAgent` | `LeagueScout/<version> (+https://github.com/mrsquidgydev/LeagueScout)` | Empty uses the default |

Guild filtering lives in the database, not in environment variables.

`/events near` geocodes the typed place with [Nominatim](https://nominatim.org/) (OpenStreetMap), configured
under `Nominatim`. The public instance allows 1 request per second and requires an identifying User-Agent, so
the bot spaces requests, caches places for 7 days and results for 15 minutes, and limits each user to one
search every 30 seconds. Typed locations are not stored or logged.

For JSON logs set `Logging__Console__FormatterName=json`.

## Run with Docker

```sh
cp .env.example .env      # then set DISCORD_TOKEN
docker compose up -d --build
docker compose logs -f
```

The container applies EF Core migrations on start, starts the PokéData refresh worker, connects to Discord,
then starts the guild sync worker. Migrations only add tables and columns; existing data is kept.
The SQLite database persists in the `bot-data` volume mounted at `/data`.

Back up the database by copying `/data/leaguescout.db` while the bot is stopped.

## Develop

Requires the .NET 10 SDK.

```sh
dotnet build LeagueScout.slnx
dotnet test LeagueScout.slnx
docker build --target test .                 # run tests in Linux
DISCORD_TOKEN=... dotnet run --project src/LeagueScout.Bot
```

Add a migration after changing the model:

```sh
dotnet tool restore
dotnet ef migrations add <Name> --project src/LeagueScout.Infrastructure --startup-project src/LeagueScout.Infrastructure --output-dir Persistence/Migrations
```

## Project layout

| Project | Responsibility |
|---|---|
| `LeagueScout.Domain` | Entities and rules: `PokemonEvent` (canonical cached event, change and missing detection), `DatasetSyncState`, `GuildConfiguration`, `GuildEventMessage`, `EventRsvp` |
| `LeagueScout.Application` | `IEventProvider`; `Caching/` (`EventDataset` keys, `EventCacheService` refresh + backoff, `IKeyedLock`, `CachedEventReader`, diagnostics); `EventSyncService` (cache to Discord); `RsvpService`, queries, `IEventMessagePublisher`, `IApplicationDbContext` |
| `LeagueScout.Infrastructure` | EF Core `AppDbContext` + SQLite migrations, `PokeDataEventProvider` (DTOs stay here, HTTP error classification), time-zone lookup |
| `LeagueScout.Bot` | Host, Discord.Net client, slash commands, RSVP buttons, embeds, `EventCacheRefreshWorker`, `EventSyncWorker` |
| `LeagueScout.Tests` | xUnit tests against in-memory SQLite with the real migrations |

### Data source

See [docs/pokedata-investigation.md](docs/pokedata-investigation.md). Summary:

- No documented API exists. The bot uses the JSON endpoint the Events v2 page itself calls (`events.php`).
- Events are identified by the official Play! Pokémon tournament ID (`pokemon_url`, e.g. `26-10-014168`).
- PokéData times are venue-local. The bot finds the venue time zone from its coordinates and stores UTC.
- The endpoint is undocumented and may change. Poll politely.

### Switching to PostgreSQL

Domain and Application have no SQLite dependency. To switch:

1. Add `Npgsql.EntityFrameworkCore.PostgreSQL` to Infrastructure.
2. Replace `UseSqlite` with `UseNpgsql` in `Infrastructure/DependencyInjection.cs` and the design-time factory.
3. Generate a fresh PostgreSQL migration set. The current migrations are SQLite-specific.
4. Copy existing data across if needed.

Discord IDs are stored as signed 64-bit integers, and timestamps as UTC, so both work on PostgreSQL.
The event cache uses no provider-specific SQL. Distance filtering uses a portable bounding-box query plus an
in-memory distance check. Refresh locking is in-process (`InProcessKeyedLock`); running several instances
would need a distributed `IKeyedLock`.
