# Graph Report - leaguescout  (2026-10-06)

## Corpus Check
- Corpus is ~17,373 words - fits in a single context window. You may not need a graph.

## Summary
- 777 nodes · 1456 edges · 40 communities (26 shown, 14 thin omitted)
- Extraction: 93% EXTRACTED · 7% INFERRED · 0% AMBIGUOUS · INFERRED: 101 edges (avg confidence: 0.85)
- Token cost: 78,037 input · 0 output

## Community Hubs (Navigation)
- Namespaces & Package Imports
- Nearby Event Search & Geocoding
- Event Embeds & RSVP Buttons
- PokeData Provider & Search Criteria
- Sync & RSVP Test Suite
- Slash Command Modules & Queries
- Discord Message Publishing
- RSVP Service & DbContext Interface
- Event Sync Service
- PokemonEvent Entity & Time Conversion
- Discord Bot Host & Interactions
- PokeData DTO Mapping
- Guild Configuration Entity
- Bot Spec & PokeData Research Docs
- Event Change Tracking
- EF Core AppDbContext
- Nominatim Geocoder
- EventBot Admin Commands
- Background Sync Worker
- Nominatim Options
- PokeData Options
- Fake Event Provider (Tests)
- Time Zone Resolution
- Solution & Core Project Files
- Test Project Dependencies
- Bot Deployment & Config
- Infrastructure Project Dependencies
- Guild Configuration Service
- Bot Options
- Launch Settings
- Sync Trigger
- Caveman Agent Rule Files
- UTC DateTime Converter
- EF Model Snapshot
- IEventProvider Interface
- Infrastructure DI Registration
- Nominatim Place DTO

## God Nodes (most connected - your core abstractions)
1. `PokemonEvent` - 71 edges
2. `GuildConfiguration` - 34 edges
3. `EventSearchCriteria` - 28 edges
4. `LeagueScout.Domain` - 28 edges
5. `GuildEventMessage` - 24 edges
6. `NearbySearchTests` - 24 edges
7. `AppDbContext` - 23 edges
8. `IApplicationDbContext` - 20 edges
9. `EventRsvp` - 20 edges
10. `NearbyEventSearchService` - 18 edges

## Surprising Connections (you probably didn't know these)
- `Polite Polling Policy` --rationale_for--> `EventSyncWorker`  [INFERRED]
  docs/pokedata-investigation.md → src/LeagueScout.Bot/Workers/EventSyncWorker.cs
- `bot-data volume` --shares_data_with--> `AppDbContext`  [INFERRED]
  docker-compose.yml → src/LeagueScout.Infrastructure/Persistence/AppDbContext.cs
- `Slash Commands (/events upcoming, mine, near; /eventbot status, configure; /donate)` --shares_data_with--> `GuildConfiguration`  [INFERRED]
  README.md → src/LeagueScout.Domain/GuildConfiguration.cs
- `Event Identity and Deduplication` --rationale_for--> `PokemonEvent`  [EXTRACTED]
  .claude/discord-bot.prompt.md → src/LeagueScout.Domain/PokemonEvent.cs
- `PokéData Event Schema` --shares_data_with--> `PokemonEvent`  [INFERRED]
  docs/pokedata-investigation.md → src/LeagueScout.Domain/PokemonEvent.cs

## Import Cycles
- None detected.

## Hyperedges (group relationships)
- **Event Sync Pipeline (provider -> normalize -> upsert -> publish)** — src_leaguescout_bot_workers_eventsyncworker_leaguescout_bot_workers_eventsyncworker, src_leaguescout_application_providers_ieventprovider_leaguescout_application_providers_ieventprovider, src_leaguescout_infrastructure_pokedata_pokedataeventprovider_leaguescout_infrastructure_pokedata_pokedataeventprovider, docs_pokedata_investigation_events_php, src_leaguescout_domain_pokemonevent_leaguescout_domain_pokemonevent, src_leaguescout_bot_discord_discordeventpublisher_leaguescout_bot_discord_discordeventpublisher [EXTRACTED 1.00]
- **Persisted Entities with Unique Indexes** — src_leaguescout_domain_pokemonevent_leaguescout_domain_pokemonevent, src_leaguescout_domain_guildconfiguration_leaguescout_domain_guildconfiguration, src_leaguescout_domain_guildeventmessage_leaguescout_domain_guildeventmessage, src_leaguescout_domain_eventrsvp_leaguescout_domain_eventrsvp [EXTRACTED 1.00]
- **Caveman Rules Distributed Across IDE Agents** — agents_caveman_rules, clinerules_caveman_rules, github_copilot_instructions_caveman_rules, opencode_agents_caveman_rules, windsurf_rules_caveman_rules [INFERRED 0.95]

## Communities (40 total, 14 thin omitted)

### Community 0 - "Namespaces & Package Imports"
Cohesion: 0.05
Nodes (21): LeagueScout.Application.Sync, LeagueScout.Domain, LeagueScout.Application.Queries, LeagueScout.Infrastructure.Persistence.Migrations, LeagueScout.Bot.Discord.Modules, LeagueScout.Application.Guilds, LeagueScout.Application, LeagueScout.Application.Persistence (+13 more)

### Community 1 - "Nearby Event Search & Geocoding"
Cohesion: 0.06
Nodes (19): GeocodedLocation, GeocodingException, IGeocoder, EventsKey, GeocodeKey, NearbyEvent, NearbyEventSearchResult, NearbyEventSearchService (+11 more)

### Community 2 - "Event Embeds & RSVP Buttons"
Cohesion: 0.06
Nodes (23): EventEmbedBuilder, RsvpButtonId, EventStatus, Active, Removed, PokemonEventType, Challenge, Cup (+15 more)

### Community 3 - "PokeData Provider & Search Criteria"
Cohesion: 0.07
Nodes (18): EventSearchCriteria, Country, EndDate, EventTypes, Game, Latitude, Longitude, Radius (+10 more)

### Community 4 - "Sync & RSVP Test Suite"
Cohesion: 0.11
Nodes (9): EventSyncTests, RsvpTests, TestDatabase, TestHarness, Clock, Database, Now, Provider (+1 more)

### Community 5 - "Slash Command Modules & Queries"
Cohesion: 0.07
Nodes (9): DependencyInjection, EventQueryService, GuildEventListing, DonateModule, EventsModule, RsvpModule, Component, CooldownKey (+1 more)

### Community 6 - "Discord Message Publishing"
Cohesion: 0.08
Nodes (15): IEventMessagePublisher, RsvpSummary, DiscordEventPublisher, GuildEventMessage, ChannelId, CreatedAt, Event, EventId (+7 more)

### Community 7 - "RSVP Service & DbContext Interface"
Cohesion: 0.08
Nodes (17): IApplicationDbContext, ChangeTracker, EventRsvps, Events, GuildConfigurations, GuildEventMessages, RsvpResult, RsvpService (+9 more)

### Community 8 - "Event Sync Service"
Cohesion: 0.14
Nodes (10): EventSyncService, SyncResult, EventsRetrieved, GuildsFailed, GuildsSynced, MessagesCreated, MessagesUpdated, NewEvents (+2 more)

### Community 9 - "PokemonEvent Entity & Time Conversion"
Cohesion: 0.08
Nodes (24): PokemonEvent, Address, City, Country, EndDateTime, EventType, FirstSeenAt, Game (+16 more)

### Community 11 - "PokeData DTO Mapping"
Cohesion: 0.11
Nodes (15): PokeDataEventDto, City, CountryCode, Game, Id, Lat, League, Lng (+7 more)

### Community 12 - "Guild Configuration Entity"
Cohesion: 0.10
Nodes (17): GuildConfiguration, Country, CreatedAt, Enabled, EventChannelId, GuildId, HasRadiusFilter, HasRegionFilter (+9 more)

### Community 13 - "Bot Spec & PokeData Research Docs"
Cohesion: 0.12
Nodes (15): Future Features (Do Not Implement Yet), MVP Definition, Phase 0 PokéData Investigation, Pokémon Premier Event Discord Bot Spec, events.php Auxiliary Actions (countries, states, cities, counts, major_events), Client-Side CSV Export, PokéData Event Schema, events.php Search Endpoint (+7 more)

### Community 14 - "Event Change Tracking"
Cohesion: 0.17
Nodes (5): EventChangeSet, Changes, HasChanges, IsMaterial, EventFieldChange

### Community 15 - "EF Core AppDbContext"
Cohesion: 0.15
Nodes (6): AppDbContext, EventRsvps, Events, GuildConfigurations, GuildEventMessages, DesignTimeDbContextFactory

### Community 19 - "Nominatim Options"
Cohesion: 0.20
Nodes (7): NominatimOptions, AttemptTimeout, BaseAddress, MaxRetryAttempts, MinRequestInterval, TotalTimeout, UserAgent

### Community 20 - "PokeData Options"
Cohesion: 0.20
Nodes (7): PokeDataOptions, AttemptTimeout, BaseAddress, MaxRetryAttempts, SourceUrl, TotalTimeout, UserAgent

### Community 21 - "Fake Event Provider (Tests)"
Cohesion: 0.20
Nodes (6): FakeEventProvider, Calls, Criteria, Events, Failure, SourceName

### Community 23 - "Solution & Core Project Files"
Cohesion: 0.25
Nodes (6): Microsoft.EntityFrameworkCore (10.0.12), Microsoft.Extensions.Caching.Memory (10.0.12), net10.0, Microsoft.NET.Sdk, net10.0, Microsoft.NET.Sdk

### Community 24 - "Test Project Dependencies"
Cohesion: 0.22
Nodes (8): coverlet.collector (6.0.4), Microsoft.Extensions.TimeProvider.Testing (10.10.0), Microsoft.NET.Test.Sdk (17.14.1), xunit (2.9.3), xunit.runner.visualstudio (3.1.4), net10.0, Microsoft.EntityFrameworkCore.Sqlite (10.0.12), Microsoft.NET.Sdk

### Community 25 - "Bot Deployment & Config"
Cohesion: 0.25
Nodes (7): bot-data volume, bot service, Discord.Net (3.20.1), Microsoft.Extensions.Hosting (10.0.12), Environment Variables (DISCORD_TOKEN, DATABASE_PATH, EVENT_SYNC_INTERVAL, DISCORD_COMMAND_GUILD_ID), Microsoft.NET.Sdk.Worker, net10.0

### Community 26 - "Infrastructure Project Dependencies"
Cohesion: 0.25
Nodes (6): GeoTimeZone (6.1.0), Microsoft.EntityFrameworkCore.Design (10.0.12), Microsoft.Extensions.Http.Resilience (10.10.0), net10.0, Microsoft.EntityFrameworkCore.Sqlite (10.0.12), Microsoft.NET.Sdk

### Community 28 - "Bot Options"
Cohesion: 0.25
Nodes (5): BotOptions, CommandGuildId, DatabasePath, DiscordToken, EventSyncInterval

### Community 29 - "Launch Settings"
Cohesion: 0.25
Nodes (7): DOTNET_ENVIRONMENT, commandName, dotnetRunMessages, environmentVariables, profiles, LeagueScout.Bot, $schema

### Community 31 - "Caveman Agent Rule Files"
Cohesion: 0.29
Nodes (6): ASD-STE100 Simplified Technical English, Caveman Response Style Rules (AGENTS.md), Caveman Rules (Cline), Caveman Rules (GitHub Copilot), Caveman Rules (OpenCode), Caveman Rules (Windsurf, always_on)

### Community 36 - "Nominatim Place DTO"
Cohesion: 0.50
Nodes (4): NominatimPlaceDto, DisplayName, Lat, Lon

## Knowledge Gaps
- **195 isolated node(s):** `net10.0`, `Microsoft.EntityFrameworkCore (10.0.12)`, `Microsoft.Extensions.Caching.Memory (10.0.12)`, `Microsoft.NET.Sdk`, `Events` (+190 more)
  These have ≤1 connection - possible missing edges or undocumented components. (Counts symbols only; 365 node(s) total have ≤1 connection when file, concept and rationale nodes are included.)
- **14 thin communities (<3 nodes) omitted from report** — run `graphify query` to explore isolated nodes.

## Suggested Questions
_Questions this graph is uniquely positioned to answer:_

- **Why does `PokemonEvent` connect `PokemonEvent Entity & Time Conversion` to `Nearby Event Search & Geocoding`, `IEventProvider Interface`, `Event Embeds & RSVP Buttons`, `PokeData Provider & Search Criteria`, `Slash Command Modules & Queries`, `Discord Message Publishing`, `RSVP Service & DbContext Interface`, `Event Identity & Dedup`, `Event Sync Service`, `Event Seen/Started Helpers`, `PokeData DTO Mapping`, `Sync & RSVP Test Suite`, `Bot Spec & PokeData Research Docs`, `Event Change Tracking`, `EF Core AppDbContext`, `Fake Event Provider (Tests)`, `Solution & Core Project Files`?**
  _High betweenness centrality (0.292) - this node is a cross-community bridge._
- **Are the 2 inferred relationships involving `PokemonEvent` (e.g. with `PokéData Event Schema` and `Venue-Local Time to UTC Conversion`) actually correct?**
  _`PokemonEvent` has 2 INFERRED edges - model-reasoned connections that need verification._
- **What connects `net10.0`, `Microsoft.EntityFrameworkCore (10.0.12)`, `Microsoft.Extensions.Caching.Memory (10.0.12)` to the rest of the system?**
  _195 weakly-connected nodes found - possible documentation gaps or missing edges._
- **Should `Namespaces & Package Imports` be split into smaller, more focused modules?**
  _Cohesion score 0.05280437756497948 - nodes in this community are weakly interconnected._
- **Why does `AppDbContext` connect `EF Core AppDbContext` to `Namespaces & Package Imports`, `UTC DateTime Converter`, `Infrastructure DI Registration`, `Discord Message Publishing`, `RSVP Service & DbContext Interface`, `PokemonEvent Entity & Time Conversion`, `Guild Configuration Entity`, `Test Project Dependencies`, `Bot Deployment & Config`, `Infrastructure Project Dependencies`?**
  _High betweenness centrality (0.109) - this node is a cross-community bridge._
- **Are the 2 inferred relationships involving `GuildConfiguration` (e.g. with `Slash Commands (/events upcoming, mine, near; /eventbot status, configure; /donate)` and `EventSyncWorker`) actually correct?**
  _`GuildConfiguration` has 2 INFERRED edges - model-reasoned connections that need verification._
- **Should `Nearby Event Search & Geocoding` be split into smaller, more focused modules?**
  _Cohesion score 0.057971014492753624 - nodes in this community are weakly interconnected._