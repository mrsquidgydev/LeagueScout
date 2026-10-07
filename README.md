# LeagueScout

Discovers nearby Pokémon TCG League Challenges and League Cups from
[PokéData Events](https://pokedata.ovh/events2/), posts each one once to a Discord channel,
and lets members RSVP with buttons (👀 Interested / ✅ Going / ❌ Not Going).

```
PokéData events.php ──► EventSyncWorker ──► upsert (SQLite) ──► Discord embed + buttons ──► RSVPs
```

## Features (MVP)

- Polls PokéData every 6 hours by default, per configured server.
- Filters by country + state/region, or by latitude/longitude/radius.
- Posts each new event once. Repeated polling never duplicates posts.
- Edits the existing post when the date, time, venue, location, type or link changes.
- Marks posts "Possibly cancelled" when an upcoming event disappears from PokéData. Reverts if it returns.
- One RSVP per user per event. Clicking another button changes it. Counts update on the post.
- Shows dates with Discord timestamps, so each user sees their own time zone.

## Commands

| Command | Who | What |
|---|---|---|
| `/events upcoming` | everyone | Next upcoming events posted in this server |
| `/events mine` | everyone | Upcoming events you marked Interested or Going |
| `/eventbot status` | everyone | This server's configuration |
| `/eventbot configure` | Manage Server | Set channel, location, event types, look-ahead, enable/disable |
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
- Saving a valid, enabled configuration queues an immediate sync.

The first sync posts every matching upcoming event in the look-ahead window. Keep the window modest.

## Environment variables

| Variable | Required | Default | Notes |
|---|---|---|---|
| `DISCORD_TOKEN` | yes | | Bot token. Never logged. |
| `DATABASE_PATH` | no | `data/leaguescout.db` (`/data/leaguescout.db` in Docker) | SQLite file; directory is created |
| `EVENT_SYNC_INTERVAL` | no | `06:00:00` | `hh:mm:ss`, minimum `00:15:00` |
| `DISCORD_COMMAND_GUILD_ID` | no | | Register commands to one server (instant). Empty = global. |

PokéData HTTP settings (timeouts, retries, user agent) live in `src/LeagueScout.Bot/appsettings.json`
under `PokeData`. Override with env vars such as `PokeData__MaxRetryAttempts=1`.
Guild filtering lives in the database, not in environment variables.

For JSON logs set `Logging__Console__FormatterName=json`.

## Run with Docker

```sh
cp .env.example .env      # then set DISCORD_TOKEN
docker compose up -d --build
docker compose logs -f
```

The container applies EF Core migrations on start, connects to Discord, then starts the sync worker.
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
| `LeagueScout.Domain` | Entities and rules: `PokemonEvent`, `GuildConfiguration`, `GuildEventMessage`, `EventRsvp`, change detection |
| `LeagueScout.Application` | `IEventProvider`, `EventSyncService`, `RsvpService`, queries, `IEventMessagePublisher`, `IApplicationDbContext` |
| `LeagueScout.Infrastructure` | EF Core `AppDbContext` + SQLite migrations, `PokeDataEventProvider` (DTOs stay here), time-zone lookup |
| `LeagueScout.Bot` | Host, Discord.Net client, slash commands, RSVP buttons, embeds, `EventSyncWorker` |
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
