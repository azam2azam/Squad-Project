# Setup

## 1. Prerequisites

| Tool | Version | Check |
|---|---|---|
| .NET SDK | 8.0.x (pinned in `global.json`) | `dotnet --version` |
| Node | 20.19+, 22.12+ or 24.x | `node --version` |
| SQL Server | 2019+ / Express | `Get-Service MSSQL*` |

Install the EF CLI once:

```bash
dotnet tool install --global dotnet-ef --version 8.*
```

## 2. Database

The default connection string in `src/Api/appsettings.json` targets
`localhost\SQLEXPRESS` with Windows authentication. Override it if your instance differs:

```bash
setx ConnectionStrings__Default "Server=.;Database=SquadStatusBoard;Trusted_Connection=True;TrustServerCertificate=True"
```

Apply the schema:

```bash
dotnet ef database update --project src/Infrastructure --startup-project src/Api
```

Running the API in `Development` does this automatically.

### Using PostgreSQL instead

```bash
setx Database__Provider "Postgres"
setx ConnectionStrings__Default "Host=localhost;Port=5432;Database=SquadStatusBoard;Username=postgres;Password=..."
```

The migrations in `src/Infrastructure/Persistence/Migrations` were generated for SQL
Server. Switching provider needs a provider-specific migration set generated against
Postgres before `database update` will succeed.

## 3. Run

**API** — `http://localhost:5220`, Swagger at `/swagger`:

```bash
dotnet run --project src/Api/Api.csproj --launch-profile http
```

**Web** — `http://localhost:4220`:

```bash
npm start --prefix web
```

The dev server proxies `/api`, `/hubs` and `/health` to port 5220 (`web/proxy.conf.json`),
so the browser makes same-origin requests and CORS does not come into play in development.

## 4. Verify

```bash
curl http://localhost:5220/health
```

Expect `Healthy`. Then open <http://localhost:4220> — the header should show a green
**API connected** chip, which only renders once role metadata has been fetched from the
API.

## 5. Seed data

On first run in Development the database is seeded with the prototype's example:

- **Board** — OPD Screen Revamp · VIDA HIS · Squad Alpha · Sprint 14 · On Track · 68%
- **Squad** — 6 members: 1 Product Owner, 1 Tech Lead, 2 Developers, 1 QA Engineer,
  1 UI/UX Designer
- **Roster** — 9 people, so the typeahead has bench depth beyond the demo squad

The seeder is idempotent: it does nothing if any board already exists. To reseed, drop
the database and re-run.

## Ports

Chosen to avoid colliding with other projects on the same machine.

| Service | Port |
|---|---|
| API (http) | 5220 |
| API (https) | 7220 |
| Web dev server | 4220 |

## Troubleshooting

**`dotnet ef` fails with a globalization error.** `InvariantGlobalization` must stay off
in `Directory.Build.props`; the EF design-time tooling needs a real culture.

**Health check returns Unhealthy.** The API can start without the database. Confirm the
SQL Server service is running and that the connection string resolves:

```bash
dotnet ef database update --project src/Infrastructure --startup-project src/Api
```

**Port already in use.** Change the port in `src/Api/Properties/launchSettings.json`
(API) or `web/angular.json` under `serve.options.port` (web), and update
`web/proxy.conf.json` plus `Cors:AllowedOrigins` to match.
