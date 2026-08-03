# Household API

RESTful API for Household — a personal home management app covering food tracking, meal planning, dish templates, household tasks, rooms, and issue reporting.

## Features

- **Food Items** — Shared ingredient/food catalogue with search
- **Dish Templates** — Per-user dish/recipe definitions
- **Meal Entries** — Log daily meals with date range filtering
- **Task Templates** — Reusable household task definitions
- **Task Entries** — Scheduled task instances linked to templates
- **Rooms** — Room catalogue for the home
- **Issues** — Report and track household issues per room
- **Admin Panel** — User management for self-hosted instances
- **JWT Authentication** — Access + refresh token flow with BCrypt password hashing
- **Seed Support** — Optional admin user seeding on first run
- **Per-user settings** — Versioned preferences, dashboard layout, provider filters, and app favorites
- **Admin invitations** — Expiring one-use invites and auditable session-aware user management
- **Jellyfin/GitHub Actions** — Encrypted server configuration, safe proxies, and a 12-repository workflow cache
- **Seerr requests** — Per-user discovery, requests, quotas, and permission-aware moderation with encrypted configuration
- **App catalog** — Database-backed launcher metadata, preferred HTTPS URLs, per-user favorites, and admin editing
- **CasaOS updates** — Admin-only, individual app-store updates with private recovery backups and conservative rollback policy

## Tech Stack

- **.NET 9.0** — ASP.NET Core Minimal API
- **Entity Framework Core 9.0** — SQLite provider
- **JWT Authentication** — BCrypt password hashing
- **Swagger/OpenAPI** — via Microsoft.AspNetCore.OpenApi

## Prerequisites

- [.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)

## Installation

```bash
cd Household.Api
cp .env.example .env
# Edit .env — set JWT_SECRET_KEY at minimum
dotnet restore
dotnet ef database update
```

## Development

```bash
dotnet run
# API available at http://localhost:5019
# Swagger UI at http://localhost:5019/swagger
```

## Production (Docker)

```bash
docker build -t household-api .
docker run -p 8080:8080 -v household-data:/data household-api
```

See the root `docker-compose.casaos.yml` for CasaOS deployment.

## API Endpoints

### Authentication

| Method | Route            | Description          |
| ------ | ---------------- | -------------------- |
| POST   | `/auth/register` | Register new user    |
| POST   | `/auth/login`    | Login                |
| POST   | `/auth/refresh`  | Refresh JWT token    |
| POST   | `/auth/logout`   | Revoke refresh token |
| POST   | `/auth/change-password` | Change the authenticated user's password and revoke all sessions |
| POST   | `/invitations/redeem` | Redeem a one-use invitation |

`POST /auth/change-password` requires a bearer token and accepts `currentPassword` (1-1024 characters) and
`newPassword` (12-128 characters, with uppercase, lowercase, number, and symbol). A successful response is
`200 { "code": "password_changed", "reauthenticationRequired": true }`; the access token used for the request and
all refresh sessions are invalid after that response. Invalid current passwords and weak new passwords return safe
`400` error codes without password data.

Users created directly by an administrator, including users created with an administrator-supplied temporary
password, and users whose password is reset must change that password before using the application. Login and
`GET /auth/me` expose `requiresPasswordChange`; access JWTs carry the same `requiresPasswordChange` claim.
While the flag is set, the API permits only `/auth/me`, `/auth/change-password`, `/auth/logout`, `/auth/logout-all`,
and anonymous `/health`; refresh-token rotation is refused and other authenticated requests return
`403 { "code": "password_change_required" }`. Invitation redemption
uses the user's chosen password and does not set the flag. A successful self-service password change clears it and
invalidates all existing access and refresh sessions.

### Food Items

| Method | Route              | Description                           |
| ------ | ------------------ | ------------------------------------- |
| GET    | `/food-items`      | List food items (optional `?search=`) |
| GET    | `/food-items/{id}` | Get food item by ID                   |
| POST   | `/food-items`      | Create a food item                    |
| PUT    | `/food-items/{id}` | Update a food item                    |
| DELETE | `/food-items/{id}` | Delete a food item                    |

### Dish Templates

| Method | Route                  | Description                |
| ------ | ---------------------- | -------------------------- |
| GET    | `/dish-templates`      | List user's dish templates |
| GET    | `/dish-templates/{id}` | Get dish template by ID    |
| POST   | `/dish-templates`      | Create a dish template     |
| PUT    | `/dish-templates/{id}` | Update a dish template     |
| DELETE | `/dish-templates/{id}` | Delete a dish template     |

### Meal Entries

| Method | Route                | Description                                  |
| ------ | -------------------- | -------------------------------------------- |
| GET    | `/meal-entries`      | List meal entries (optional `?from=` `?to=`) |
| GET    | `/meal-entries/{id}` | Get meal entry by ID                         |
| POST   | `/meal-entries`      | Create a meal entry                          |
| PUT    | `/meal-entries/{id}` | Update a meal entry                          |
| DELETE | `/meal-entries/{id}` | Delete a meal entry                          |

### Task Templates

| Method | Route                  | Description             |
| ------ | ---------------------- | ----------------------- |
| GET    | `/task-templates`      | List task templates     |
| GET    | `/task-templates/{id}` | Get task template by ID |
| POST   | `/task-templates`      | Create a task template  |
| PUT    | `/task-templates/{id}` | Update a task template  |
| DELETE | `/task-templates/{id}` | Delete a task template  |

### Task Entries

| Method | Route                | Description          |
| ------ | -------------------- | -------------------- |
| GET    | `/task-entries`      | List task entries    |
| GET    | `/task-entries/{id}` | Get task entry by ID |
| POST   | `/task-entries`      | Create a task entry  |
| PUT    | `/task-entries/{id}` | Update a task entry  |
| DELETE | `/task-entries/{id}` | Delete a task entry  |

### Rooms

| Method | Route         | Description    |
| ------ | ------------- | -------------- |
| GET    | `/rooms`      | List rooms     |
| GET    | `/rooms/{id}` | Get room by ID |
| POST   | `/rooms`      | Create a room  |
| PUT    | `/rooms/{id}` | Update a room  |
| DELETE | `/rooms/{id}` | Delete a room  |

### Issues

| Method | Route          | Description     |
| ------ | -------------- | --------------- |
| GET    | `/issues`      | List all issues |
| GET    | `/issues/{id}` | Get issue by ID |
| POST   | `/issues`      | Create an issue |
| PUT    | `/issues/{id}` | Update an issue |
| DELETE | `/issues/{id}` | Delete an issue |

### Admin

| Method | Route               | Description    |
| ------ | ------------------- | -------------- |
| GET    | `/admin/users`      | List all users |
| POST   | `/admin/users`      | Create a user with a temporary password that must be changed |
| PATCH  | `/admin/users/{id}` | Edit role/name/email/active state |
| POST   | `/admin/users/{id}/reset-password` | Issue a secure temporary password that must be changed |
| POST   | `/admin/invitations` | Create an expiring invitation |

### Settings And Monitors

| Method | Route | Description |
| --- | --- | --- |
| GET/PATCH | `/api/v1/preferences` | Current user's versioned preferences |
| GET/PATCH | `/api/v1/dashboard/layout` | Independent widget layout |
| GET | `/api/v1/dashboard/catalog` | Stable widget catalog |
| POST | `/api/v1/dashboard/layout/reset` | Restore default layout |
| GET | `/api/v1/jellyfin/dashboard` | Continue Watching and Next Up |
| GET | `/api/v1/github-actions` | Cached allowlisted workflow status |
| GET | `/api/v1/seerr/session` | Current mapped Seerr identity, permissions, and quotas |
| GET | `/api/v1/seerr/search` | Permission-aware Seerr search |
| GET/POST | `/api/v1/seerr/requests` | Request history and request creation |
| GET/PATCH | `/api/v1/admin/apps/catalog/{id?}` | Admin launcher catalog editing |

Jellyfin item links are browser-session deep links. Depending on the Jellyfin deployment and current browser login,
opening one may show Jellyfin's sign-in page rather than the item; Household never places the Jellyfin API key in
the link or browser session.

### CasaOS Update Operations (Admin Only)

Household creates a private Compose recovery backup, then queues an individual CasaOS update. It uses `PATCH /v2/app_management/compose/{projectName}?force=true` for AppStore projects and falls back to reapplying the current Compose with `PUT` for self-published projects. Catalog IDs map explicitly to CasaOS projects, such as `seerr` to `big-bear-seerr`. Immich is monitor/open-only, CasaOS is link-only, and there is no bulk update endpoint.

| Method | Route | Body / result |
| --- | --- | --- |
| GET/PUT | `/api/v1/admin/casaos/config` | Write-only internal base URL/raw token configuration. |
| POST | `/api/v1/admin/casaos/apps/{appId}/update` | No body; returns HTTP 202 queued/accepted metadata. |
| POST | `/api/v1/admin/casaos/apps/{appId}/rollback` | Conservatively returns `rollback_not_safe` until automated eligibility can be proven. |
| GET | `/api/v1/admin/casaos/apps/{appId}/actions` | Latest 50 update/rollback audit records. |
| GET | `/api/v1/admin/casaos/apps/{appId}/actions/{actionLogId}` | One accepted/failed record; it is not live CasaOS completion status. |

CasaOS returns before the asynchronous pull/apply completes. Household therefore records the operation as `Queued`, never as completed or succeeded. Backups remain available for manual recovery, but a backup ID alone never enables automated rollback because Compose YAML cannot restore mutable images, volumes, or application data.

Successful update response shape is `{ actionLogId, appId, action, status: "Queued", message, startedAt, backupId, safetyBackupId }`. History also returns `rollbackAvailable`, `finishedAt`, `previousImages`, and a safe `errorCode`; it never returns YAML or filesystem paths.

`GET /modules/apps/` and `GET /modules/apps/{id}` expose nullable `updateAvailable` plus explicit `monitoringEnabled`, `canUpdate`, and `canRollback` capabilities. Runtime catalog reads come from SQLite; a mounted JSON file is only an insert-only bootstrap importer.

Provider quick actions and assets remain under `/modules`: Games status reconciliation, timezone-aware DoIt, exact seven-day Jellywatch plus poster proxy, Pokemon sprite/download proxy, and Warcraft tracking status.

`GET /modules/today?date=YYYY-MM-DD` always forwards an explicit date and the authenticated user's stored preference `timeZoneId` to DoIt. If `date` is omitted, Household derives it in that stored timezone (or UTC until a timezone is stored). Complete and undo accept the same optional `date` query; when omitted they recover the exact date/timezone from the recently loaded occurrence, and fail safely if that context is unavailable. Mutation requests and ambiguous-timeout reconciliation use that same explicit date/timezone. Today task DTOs preserve `recurrenceType`, `assignmentMode`, `assigneeNames`, `timeZoneId`, and nullable UTC `completedAt`.

## Project Structure

```
Household.Api/
├── Configuration/        # Strongly-typed settings (JWT, CORS, DB, Seed)
├── Data/                 # EF Core DbContext
├── DTOs/                 # Request/Response DTOs
├── Endpoints/            # Minimal API endpoint maps
│   ├── AdminEndpoints.cs
│   ├── AuthEndpoints.cs
│   ├── DishEndpoints.cs
│   ├── FoodItemEndpoints.cs
│   ├── IssueEndpoints.cs
│   ├── MealEndpoints.cs
│   ├── RoomEndpoints.cs
│   └── TaskEndpoints.cs
├── Helpers/              # JWT claims helpers, extension methods
├── Middleware/           # Exception handling middleware
├── Migrations/           # EF Core migrations
├── Models/
│   ├── Auth/             # User, RefreshToken entities + settings
│   ├── Food/             # FoodItem, DishTemplate, MealEntry entities
│   └── Home/             # Room, Issue, TaskTemplate, TaskEntry entities
├── Services/             # Business logic (IDishService, ITaskService, ...)
└── Program.cs            # App bootstrap, DI, middleware pipeline
```

## Environment Variables

| Variable                   | Description                     | Default              |
| -------------------------- | ------------------------------- | -------------------- |
| `DATABASE_PATH`            | SQLite database file path       | `/data/household.db` |
| `JWT_SECRET_KEY`           | JWT signing key (32+ chars)     | _(required)_         |
| `JWT_ISSUER`               | JWT issuer claim                | `Household.Api`      |
| `JWT_AUDIENCE`             | JWT audience claim              | `Household.Client`   |
| `JWT_ACCESS_TOKEN_MINUTES` | Access token lifetime (minutes) | `15`                 |
| `JWT_REFRESH_TOKEN_DAYS`   | Refresh token lifetime (days)   | `30`                 |
| `CORS_ALLOWED_ORIGINS`     | Comma-separated allowed origins | _(empty)_            |
| `SEED_ADMIN_ENABLED`       | Create admin user on first run  | `false`              |
| `SEED_ADMIN_EMAIL`         | Admin user email                | `admin@local`        |
| `SEED_ADMIN_USERNAME`      | Admin username                  | `admin`              |
| `SEED_ADMIN_PASSWORD`      | Admin password                  | _(set in .env)_      |
| `GITHUB_ACTIONS_POLL_SECONDS` | Workflow polling interval (45-90 seconds) | `60` |
| `GITHUB_ACTIONS_CONCURRENCY` | Bounded workflow poll concurrency | `4` |
| `WARCRAFT_STATUS_PATH_TEMPLATE` | Provider tracking-status route override | documented default |
| `POKEMON_DOWNLOAD_PATH_TEMPLATE` | Provider Pokemon-download route override | documented default |
| `CASAOS_COMPOSE_BACKUP_ROOT` | Private persistent compose backup root | `/data/compose-backups` |
| `CASAOS_UPDATE_TIMEOUT_SECONDS` | CasaOS request timeout (clamped 5-30s) | `15` |
| `CASAOS_MAX_YAML_BYTES` | Maximum fetched/stored compose YAML | `2097152` |
| `CASAOS_MAX_JSON_BYTES` | Maximum upgradable-app JSON | `262144` |

Jellyfin URLs/API key and the GitHub read-only fine-grained PAT are admin-set, write-only, purpose-protected database values. Household stores no Jellyfin passwords.

CasaOS internal base URL and raw JWT are likewise admin-set and purpose-protected. Use a LAN/host address reachable from the bridged Household container, not `localhost`/`127.0.0.1`; persist `/data/compose-backups` and keep it readable only by the API container identity. No Docker socket or CasaOS app directory mount is required.

The API reads local `.env` values only when the same process environment variable is not already set, so Docker/CasaOS configuration keeps precedence.

## License

MIT

## Stack

- **.NET 9** ASP.NET Core Minimal API
- **Entity Framework Core** + **SQLite**
- **JWT Bearer** authentication
- **BCrypt** password hashing

## Getting Started

```bash
dotnet restore
dotnet run
```

API runs at `http://localhost:5019`.

Interactive docs at `http://localhost:5019/swagger`.

## Environment Variables

Set these in `appsettings.Development.json` or as env vars:

| Variable               | Description                       | Default                 |
| ---------------------- | --------------------------------- | ----------------------- |
| `Jwt__Secret`          | JWT signing secret (min 32 chars) | —                       |
| `Jwt__Issuer`          | JWT issuer                        | `HouseholdApi`          |
| `Jwt__Audience`        | JWT audience                      | `HouseholdApp`          |
| `Jwt__ExpiryMinutes`   | Token lifetime (minutes)          | `60`                    |
| `Cors__AllowedOrigins` | Comma-separated allowed origins   | `http://localhost:5173` |
| `Seed__AdminEmail`     | Admin user email                  | —                       |
| `Seed__AdminPassword`  | Admin user password               | —                       |

See `.env.example` for a reference.

## Migrations

```bash
dotnet ef migrations add <MigrationName>
dotnet ef database update
```

## Docker

```bash
# Build image
docker build -t household-api .

# Run
docker run -p 1127:8080 \
  -e Jwt__Secret=your-secret-here \
  -e Seed__AdminEmail=admin@example.com \
  -e Seed__AdminPassword=Admin1234! \
  household-api
```

Or use the provided `docker-compose.casaos.yml` for CasaOS deployment.

## CI / CD

GitHub Actions builds and pushes a multi-arch image to GHCR on every push to `main` or `master`:

```
ghcr.io/david-h-afonso/household-api:latest
```

## Project Structure

```
Household.Api/
  Data/              EF Core DbContext
  Endpoints/         Route handlers grouped by domain
  Migrations/        EF Core migrations
  Models/            Domain entities and DTOs
  Services/          Business logic (auth, food, tasks, etc.)
  Program.cs         App entry point and DI setup
```
