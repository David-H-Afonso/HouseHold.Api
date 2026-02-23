# Household API

.NET 9 Minimal API backend for the Household app. Handles authentication, food tracking, dish templates, meal entries, and household tasks.

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
