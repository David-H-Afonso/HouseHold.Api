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
| POST   | `/admin/users`      | Create a user  |
| PUT    | `/admin/users/{id}` | Update a user  |
| DELETE | `/admin/users/{id}` | Delete a user  |

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
