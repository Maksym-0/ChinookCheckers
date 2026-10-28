# Chinook Checkers API

REST Web API that accepts an English checkers (8x8) position in **PDN** and returns the best move.
The engine is **KingsRow** (Ed Gilbert), driven through its native CheckerBoard API
(`Kingsrow64.dll`) and backed by the KingsRow English 2–8 piece WLD endgame databases.
A small web UI is included to play against the engine and to verify that the service works.

## Features

- `POST /v1/move/suggest`: best move, principal variation, score or WDL, depth, nodes
- `POST /v1/move/validate`: legality check of a move
- `POST /v1/move/legal`: all legal moves with resulting positions (used by the web UI)
- `GET /healthz`: liveness and worker count
- Strength levels `weak` / `medium` / `strong`
- Endgame tablebase for positions with 8 pieces or fewer
- Pool of long-lived engine workers, LRU cache (15 min), soft and hard time limits
- JSON request logging (`timeMs`, `depth`, `nodes`, `tablebaseHit`, `cacheHit`, `RequestId`)
- Built-in web UI served from `/`

## Architecture

Clean Architecture, dependencies point inwards:

| Project | Responsibility |
|---|---|
| `ChinookCheckers.Core` | Domain: PDN parser, move generator, positions, options. No dependencies. |
| `ChinookCheckers.Application` | Use cases: `MoveService`, `SearchLimitsResolver`, `LruCache`, DTOs, engine pool abstraction. |
| `ChinookCheckers.Infrastructure` | Native engine integration: `Kingsrow64.dll` interop, adapter, worker pool. Windows x64 only. |
| `ChinookCheckers.API` | ASP.NET Core controllers, error mapping, static web UI (`wwwroot`). |
| `tests/*` | Unit tests for Core and Application (cross-platform). |

### Request flow

1. Parse and normalize the PDN; validate squares and piece counts.
2. Look up the canonical position and level in the LRU cache.
3. Resolve limits from the level (see below). Positions with 8 pieces or fewer are resolved from the endgame database.
4. Run the search on a free worker.
5. **The engine output is never trusted**: the move is matched against our own legal move list
   (resulting board, then reported notation, then first PV move). Otherwise the request fails with 500.
6. Cache and return the result.

### Concurrency

- On startup the app creates `Engine:Workers` engine workers and warms them up.
- The engine keeps global state, so every worker loads **its own copy** of the DLL
  (`Kingsrow64.worker{N}.dll`) and runs on a dedicated thread.
- Requests wait for a free worker in a bounded queue. The hard timeout covers the wait and the search.
- `softTimeMs` is enforced inside the adapter. `hardTimeMs` is enforced in the controller
  with a `CancellationToken` and results in `504`.

## Strength levels

Difficulty is controlled by thinking time only:

| Level | Soft time | Notes |
|---|---|---|
| `weak` | 100 ms | |
| `medium` | 250 ms | |
| `strong` | 500 ms | Tablebase is probed first. |
| *(none)* | 300 ms | Default soft limit. |

Positions with 8 pieces or fewer use the endgame database (about 30 ms) regardless of level.
Explicit `limits` in the request override the level. Soft time is capped at `hardTimeMs` minus a safety margin.

## Getting started

### Requirements

- Windows x64 (the engine is a native Windows library)
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- KingsRow endgame databases (optional, about 3.4 GB; see below)

### 1. Endgame databases

The databases are not included in the repository because of their size. Without them the API still works,
but returns no tablebase hits. Follow `ChinookCheckers.Infrastructure/Engine/db/_README-DOWNLOAD-DB-HERE.txt`:

1. Download **"Kingsrow English 2 through 8 pieces WLD db"** from
   <https://edgilbert.org/EnglishCheckers/KingsRowEnglish.htm>.
2. Run the installer and install it into a folder of your choice (the `kr_english_wld` subfolder is created).
3. Point the app to that folder (next section).

### 2. Configure

Set `Engine:Databases` to the folder that contains `kr_english_wld`. A relative path is resolved against
the application base directory. The easiest way is an environment variable:

```powershell
$env:Engine__Databases = "D:\kingsrow\db"
```

### 3. Run

```powershell
dotnet run --project ChinookCheckers.API --launch-profile http
```

- Web UI: <http://localhost:5157/>
- Health check: <http://localhost:5157/healthz>
- OpenAPI document (Development only): <http://localhost:5157/openapi/v1.json>

Open the UI through the API URL, not as a `file://` page.

## Configuration

`ChinookCheckers.API/appsettings.json`:

| Setting | Default | Description |
|---|---|---|
| `Engine:Type` | `kingsrow_dll` | Engine integration type. |
| `Engine:Name` | `chinook` | Engine name returned in API responses. |
| `Engine:Path` | `Engine\app\Kingsrow64.dll` | Engine DLL (relative to the app base directory). |
| `Engine:Workers` | `2` | Number of engine workers. |
| `Engine:Databases` | – | Folder with the WLD databases. Empty disables tablebase. |
| `Engine:TablebaseMaxPieces` | `8` | Maximum piece count resolved from the database. |
| `Engine:HashSizeMb` / `DbCacheMb` | `64` / `512` | Per worker. RAM is roughly `Workers * DbCacheMb`. |
| `Engine:WarmupTimeMs` | `100` | Warm-up search per worker on startup. |
| `Cache:Capacity` / `TtlMinutes` | `20000` / `15` | LRU cache with absolute TTL. |
| `Limits:DefaultSoftTimeMs` / `DefaultHardTimeMs` | `300` / `1200` | Used when the request has no limits. |
| `Limits:MaxHardTimeMs` | `10000` | Upper bound for client-supplied `hardTimeMs`. |
| `Limits:Weak` / `Medium` / `Strong` | see above | Time per level. `Depth` is informational. |

Any setting can be overridden by an environment variable, for example `Engine__Workers=1`.

## API

### `POST /v1/move/suggest`

```json
{
  "gameId": "checkers-8x8",
  "state": { "notation": "PDN", "position": "B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16" },
  "level": "weak",
  "limits": { "maxDepth": 12, "softTimeMs": 250, "hardTimeMs": 1200 }
}
```

`level` and `limits` are optional.

```json
{
  "engine": "chinook",
  "bestMove": "14x23",
  "pv": ["14x23", "27x18", "16x23", "25-21", "12-16", "28-24", "16-19", "24x15", "10x19", "22-17", "19-24"],
  "scoreOrWDL": 158,
  "depth": 18,
  "nodes": 893685,
  "positionKey": "pdn:B:W18,19,22,25,27,28,30,32:B1,5,6,7,10,12,14,16",
  "info": { "tablebaseHit": false, "timeMs": 229 }
}
```

- `scoreOrWDL`: engine evaluation, or `1` / `0` / `-1` (win / draw / loss for the side to move) on a tablebase hit.
- `positionKey`: canonical PDN (sorted squares, kings prefixed with `K`). Different spellings of one position share it.
- Accepted PDN forms: `B:W18,19,K22:B1,5-7`, optionally wrapped as `[FEN "..."]`; case-insensitive.

### `POST /v1/move/validate`

```json
{ "position": "B:W21-32:B1-12", "move": "9-13" }
```

Response: `{ "legal": true }`. Multi-jumps may be written in full (`9x18x27`) or short (`9x27`).

### `POST /v1/move/legal`

```json
{ "position": "B:W21-32:B1-12" }
```

Returns `sideToMove`, `winner` (only when the side to move has no moves) and every legal move with
`notation`, `path`, `captured` and `resultingPosition`.

### `GET /healthz`

```json
{ "ok": true, "workers": 2 }
```

### Errors

Errors use RFC 9457 problem details.

| Status | When |
|---|---|
| `400` | Malformed JSON, missing or out-of-range fields, unknown `level`. |
| `422` | Invalid PDN, unsupported `gameId` or `notation`, position with no legal moves. |
| `504` | No move within `hardTimeMs`. |
| `503` | The engine pool is not ready. |
| `500` | Engine failure, or the engine returned an illegal move. |

## Web UI

Served from `/`. Plain HTML, CSS and ES modules, no build step.

- Play as White, Black or random; choose the engine level.
- **Hint** asks the API for the best move (always at `strong`).
- Undo, board flip, square numbers.
- **Position editor** (PDN) to start from any position.
- Draw detection (threefold repetition, 40 moves without a capture or man move).
- The UI contains no rules logic: legal moves come from `/v1/move/legal`.

## Tests and CI

```powershell
dotnet test ChinookCheckers.slnx
```

- `ChinookCheckers.Core.Tests`: PDN parsing, move generation (including perft against reference values), notation.
- `ChinookCheckers.Application.Tests`: LRU cache (capacity, TTL, eviction), limits resolution, `MoveService`
  with a mocked engine pool.
- `Infrastructure` (native DLL) is intentionally not unit tested, so the suite runs on any OS.
- GitHub Actions (`.github/workflows/ci.yml`) runs the tests on `ubuntu-latest` with .NET 10.

## Hosting on IIS

The app is a regular ASP.NET Core application and does not need a Windows Service.

1. Install the **.NET 10 Hosting Bundle** on the server.
2. `dotnet publish ChinookCheckers.API -c Release -o <folder>`
3. Create an IIS site pointing at `<folder>`. Application pool: **No Managed Code**, 64-bit
   (`Enable 32-Bit Applications = False`).
4. Grant the app pool identity **write** access to `<folder>\Engine\app` (the per-worker DLL copies are created there)
   and **read** access to the database folder.
5. Set `Engine:Databases` to an absolute path (for example via `appsettings.Production.json` or an environment variable).
6. Check `GET /healthz`.

## Known limitations

- Windows x64 only; the engine runs inside the web process. A native crash would take the host down.
- `limits.maxDepth` is accepted but not enforced (the CheckerBoard API has no depth limit); levels differ by time only.
- The cache key is the canonical position and level; `limits` are not part of it.
- Positions from the engine opening book are answered instantly and identically on every level.
- `weak` is still a strong player; it only has less time than `medium` and `strong`.
- The tablebase is not used when the side to move has a pending capture; the engine searches instead.

## Third-party components

The KingsRow engine (`Kingsrow64.dll`, `egdb64.dll`, opening book and weights) and its databases are the work of
Ed Gilbert. See <https://edgilbert.org/EnglishCheckers/KingsRowEnglish.htm> for terms of use.
