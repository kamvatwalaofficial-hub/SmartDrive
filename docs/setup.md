# Setup

## Prerequisites

| Tool | Version | Needed for |
|---|---|---|
| .NET SDK | 10.0 (see `backend/global.json`) | API and tests |
| Node.js | 22 or later | Frontend |
| Docker | any recent | Optional: local SQL Server |
| `dotnet-ef` | 10.0.x | Optional: creating or applying migrations by hand |

## Option A — quick demo with SQLite (no database server)

```bash
cd backend
dotnet run --project src/SmartDrive.Api --launch-profile "SmartDrive.Api (SQLite quick demo)"
```

This profile sets `Database__Provider=Sqlite` and creates `smartdrive-demo.db` in the API folder with `EnsureCreated`, then seeds 18 months of sample history. Delete the file to start again.

## Option B — SQL Server (the default provider)

1. Start SQL Server in Docker:

   ```bash
   cp .env.example .env             # then edit MSSQL_SA_PASSWORD
   docker compose up -d
   ```

2. Give the API its connection string without committing it, using user secrets:

   ```bash
   cd backend/src/SmartDrive.Api
   dotnet user-secrets set "ConnectionStrings:SmartDrive" "Server=localhost,1433;Database=SmartDrive;User Id=sa;Password=<your password>;TrustServerCertificate=True"
   ```

3. Run the API. In Development, `Database:ApplyMigrationsOnStartup` is `true`, so migrations are applied and the sample data is seeded on first start:

   ```bash
   cd backend
   dotnet run --project src/SmartDrive.Api
   ```

   API: <http://localhost:5080> · Swagger UI: <http://localhost:5080/swagger> · Health: <http://localhost:5080/health>

## Frontend

```bash
cd frontend
npm ci
npm run dev          # http://localhost:5173
```

The Vite dev server proxies `/api` and `/media` to `http://localhost:5080` (override with `VITE_API_PROXY_TARGET`). For a build that talks to an API on another origin, set `VITE_API_BASE_URL` and add that origin to the API's `Cors:AllowedOrigins`.

## Configuration

ASP.NET Core reads `appsettings.json`, then `appsettings.{Environment}.json`, then user secrets (Development) and environment variables (`__` separates sections, e.g. `Jwt__SigningKey`). `.env.example` lists every variable.

| Setting | Default | Notes |
|---|---|---|
| `Database:Provider` | `SqlServer` | `Sqlite` for the quick demo |
| `ConnectionStrings:SmartDrive` | — | Never commit a real one |
| `Database:ApplyMigrationsOnStartup` | `false` (`true` in Development) | Production should apply migrations as a deployment step |
| `Jwt:SigningKey` | empty | **Required outside Development/Testing** — at least 32 random bytes. In Development a random key is generated per run, so tokens do not survive restarts. |
| `Jwt:ExpiryMinutes` | 120 | |
| `Cors:AllowedOrigins` | `http://localhost:5173` | |
| `Api:ExposeDocumentation` | `false` (`true` in Development) | Serves `/openapi/v1.json` and `/swagger` |
| `Seed:Enabled`, `Seed:SampleData` | `false` (`true` in Development) | Creates roles, demo accounts and sample data |
| `Seed:AdminPassword`, `Seed:CustomerPassword` | Development values | Local demo accounts only |
| `Rental:*` | see `appsettings.json` | VAT, grace period, discounts, one-way fee, turnaround, hold time, cancellation window |
| `RateLimiting:*` | 10 / 40 / 10 / 5 | Per minute for auth, recommendations, payments; per 10 minutes for the contact form |
| `Ai:LanguageModel:BaseUrl`, `ApiKey`, `Model` | empty | Optional OpenAI-compatible endpoint for free-text interpretation |
| `Media:MaxBytes` | 5 MB | Upload limit for vehicle images |

## Migrations

Migrations live in `backend/src/SmartDrive.Application/Data/Migrations`.

```bash
cd backend
dotnet ef migrations add <Name> --project src/SmartDrive.Application --startup-project src/SmartDrive.Api --output-dir Data/Migrations
dotnet ef database update --project src/SmartDrive.Application --startup-project src/SmartDrive.Api
```

The design-time factory uses `SMARTDRIVE_DESIGN_CONNECTION` if set. Without a local SDK, run the **Generate EF Core migration** workflow from the Actions tab; it opens a pull request with the generated files. CI fails if the model has changes that no migration covers, and applies every migration to a real SQL Server.

## Production checklist

- Set `ASPNETCORE_ENVIRONMENT=Production`, a strong `Jwt__SigningKey` and the connection string via a secret store.
- Leave `Seed:Enabled` off (or set `SampleData` false and supply your own admin credentials once).
- Apply migrations as a release step.
- Serve the built frontend (`frontend/dist`) from a static host or CDN and restrict `Cors:AllowedOrigins` to it.
- Put the API behind HTTPS; HSTS is enabled outside Development.
