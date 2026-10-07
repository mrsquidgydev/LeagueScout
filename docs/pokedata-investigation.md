# PokéData Events v2 — Data Interface Investigation

Investigated 2026-10-06 by reading the page source of `https://pokedata.ovh/events2/`
(`index.html` + `script.js`) and replaying the same GET requests the page issues.
No authentication, access control, or other restriction was bypassed: every request below is
an anonymous GET that the public page itself performs.

## Summary

| Question | Finding |
|---|---|
| Documented public API? | **No.** No API documentation found. `https://pokedata.ovh/api/` is a bare Apache directory listing (`complete/`, `complete2/`, `simple/`) unrelated to events v2. |
| Data source used by Events v2 | Single JSON endpoint: `GET https://pokedata.ovh/events2/events.php` |
| HTTP method | `GET` only (`Access-Control-Allow-Methods: GET`) |
| Response format | `application/json; charset=UTF-8` — a bare JSON array of event objects (no envelope) |
| Pagination | **None.** All matches return in one response (334 Texas TCG Challenges/Cups over ~8 weeks ≈ 127 KB). |
| Rate limiting | None identified. No `RateLimit`/`Retry-After` headers. Served through Cloudflare (`cf-cache-status: DYNAMIC`), so Cloudflare may throttle abusive clients. |
| Intended for public consumption? | **Not explicitly.** It is the site's own backend endpoint (CORS `Access-Control-Allow-Origin: https://pokedata.ovh`, page sends `X-Requested-With: XMLHttpRequest`). It is unauthenticated and publicly reachable, but undocumented and may change without notice. Poll politely (hours, not minutes). |
| Stable unique IDs? | **Yes, two.** `id` (PokéData GUID) and `pokemon_url` (official Play! Pokémon tournament ID, e.g. `26-10-014168`). Both unique within responses and identical across different query types (72/72 overlapping events matched between a state query and a radius query). |
| CSV export | **Client-side only.** The "Export to CSV" button calls the same `events.php` search endpoint and builds the CSV in the browser. There is no server CSV endpoint. |
| Conditional requests | **Not supported.** Re-checked 2026-10-07: responses carry no `ETag`, `Last-Modified`, `Cache-Control` or `Retry-After` header (`Transfer-Encoding: chunked`, `cf-cache-status: DYNAMIC`). The bot sends unconditional requests. |
| Country-wide query | **Works.** `country=US` with empty `stateCodes` and no radius returned every US event: 2,173 TCG Challenges/Cups for 2026-10-06..2026-11-06, 838 KB, 1.7 s (2026-10-07). |

Integration choice: **option 2 — stable endpoint legitimately used by Events v2** (`events.php` search).

## Endpoint: event search

```
GET https://pokedata.ovh/events2/events.php?<params>
X-Requested-With: XMLHttpRequest
```

The page builds every search with `URLSearchParams` containing all keys; empty values mean "no filter".

| Parameter | Example | Meaning |
|---|---|---|
| `includePast` | `false` | Include past events |
| `leagueMode` | `false` | UI "League Mode" (groups by league); keep `false` |
| `country` | `US` | ISO-3166 alpha-2 country code (list from `action=countries`) |
| `stateCodes` | `Texas` | Comma-separated **region names**, not codes (list from `action=states`). `TX` returns `[]`; `Texas` works. |
| `city` | `Dallas` | City text filter |
| `shop` | | Shop-name text filter |
| `spatialMode` | `radius` | `radius` or `zone` (polygon) |
| `polygonPoints` | | `lat,lng;lat,lng;...` when `spatialMode=zone` |
| `lat`, `lng` | `32.7767`, `-96.797` | Radius search centre (only when `spatialMode=radius`) |
| `radius` | `50` | Radius value |
| `unit` | `mi` | `mi` or `km` |
| `startDate`, `endDate` | `2026-10-06`, `2026-11-30` | `yyyy-MM-dd`, inclusive. UI defaults `startDate` to today. Verified: results fell inside the supplied range. |
| `tcgTypes` | `cups,challenges` | Comma list of `cups`, `challenges`, `prerelease`, `friendly`. Empty = none of that game. |
| `vgTypes` | | Comma list of `cups`, `challenges`, `friendly` |
| `goTypes` | | Comma list of `cups`, `challenges`, `friendly` |

How each UI action maps to this request:

1. **TCG selected / 2. Cups / 3. Challenges** — sidebar toggles change `tcgTypes` (and `vgTypes`/`goTypes`). TCG Cups + Challenges only = `tcgTypes=cups,challenges&vgTypes=&goTypes=`.
2. **US state selected** — `country=US&stateCodes=Texas` (names from `action=states&country=US`).
3. **Radius search** — `spatialMode=radius&lat=..&lng=..&radius=..&unit=mi`. Verified: radius 50 mi around Dallas returned 72 events, max `distance` 40.0 (miles). The UI clears country/state filters when a radius is used.
4. **Date range** — `startDate` / `endDate`.
5. **CSV export** — same request as search (without `leagueMode`/`spatialMode`/`polygonPoints`), converted to CSV in the browser.

Note: if no country/city/shop/spatial filter is set, the UI refuses to search. The bot always sends one.

### Event schema (one array element)

```json
{
  "id": "9cd8a811-e4d1-4f30-a65a-e88e1473a7be",
  "title": "Cantu Collectibles TCG Challenge",
  "when": "2026-10-06 19:00:00",
  "shopName": "CANTU COLLECTIBLES",
  "city": "Webster",
  "state": "Texas",
  "country_code": "US",
  "lat": 29.5459,
  "lng": -95.1267,
  "pokemon_url": "26-10-014168",
  "league": "26048756",
  "distance": 6526.527133840248,
  "game": "tcg",
  "type": "challenges",
  "day": "06",
  "month": "Oct",
  "isPast": false
}
```

| Field | Type | Notes |
|---|---|---|
| `id` | string (GUID) | PokéData record ID |
| `title` | string | Organizer-supplied name; free text, inconsistent |
| `when` | string `yyyy-MM-dd HH:mm:ss` | **Venue-local wall-clock time, no offset.** Evidence: common start times 12:00, 13:00, 18:30, 19:00. Must be converted to UTC using the venue's time zone (derived from `lat`/`lng`). |
| `shopName` | string | Usually upper case |
| `city`, `state` | string | `state` is the full region name |
| `country_code` | string | ISO alpha-2 |
| `lat`, `lng` | number | Venue coordinates |
| `pokemon_url` | string | Official tournament ID. Page builds `https://www.pokemon.com/us/pokemon-trainer-club/play-pokemon-tournaments/{pokemon_url}/` (or uses the value as-is when it is already absolute). |
| `league` | string | Play! Pokémon league ID; league page `https://www.pokemon.com/us/play-pokemon/pokemon-events/leagues/{league}/` |
| `distance` | number | Distance from search centre in the request `unit`; meaningless when no radius is given |
| `game` | string | `tcg`, `vg`, `go` |
| `type` | string | `cups`, `challenges`, `prerelease`, `friendly` |
| `day`, `month`, `isPast` | | Display helpers derived from `when` |

No address, postal code, end time, registration-status, or cancellation field is present.
A cancelled event can therefore only be detected by **disappearing** from results.

## Other actions on the same endpoint

| Request | Response |
|---|---|
| `?action=countries` | `["AE","AR",...,"US",...]` |
| `?action=states&country=US` | `["Alabama",...,"Texas",...]` (region names) |
| `?action=cities&searchQuery=..&country=..` | City autocomplete |
| `?action=counts&<search params>` | `{"League Cup":88,"League Challenge":246,"Pre-Release":0,...}` — ignores type filters |
| `?action=major_events&country=..&stateCodes=..` | Regionals/Internationals (out of MVP scope) |
| `?league=<id>&includePast=true` | All events for one league |

## Decisions for the bot

- **Identity:** `Source = "pokedata"`, `SourceEventId = pokemon_url` (official Play! Pokémon tournament ID).
  It is stable by definition, unique, and survives a PokéData re-import, unlike a database GUID. Records without it are skipped and logged.
- **Times:** parse `when` as local time, resolve the IANA time zone from `lat`/`lng`, store UTC.
- **Filtering:** both `country + stateCodes` and `lat/lng/radius` are verified. The bot supports either per guild; radius wins when set.
- **Cancellation:** an upcoming event missing from 2 successful, non-empty refreshes in a row of a dataset that
  covers it is marked `Removed` ("may be cancelled"). Failed refreshes never count. It returns to `Active` if it reappears.
- **Polling:** guilds do not poll. Each distinct dataset (one per country for region filters, one per rounded
  centre and radius for radius filters) is requested at most every 6 hours plus jitter, with persisted backoff after
  failures. Guilds read the shared cache. See the README section "How PokéData is polled".
