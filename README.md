# Squad Status Board

A production web application for composing, maintaining and presenting live status
snapshots for engineering squads inside the **Product Innovation & Revamp Team (PIRT)**.

A delivery leader fills in a builder form on the left and a boardroom-quality dark slide
renders live on the right — progress ring, squad-composition bar, role-coloured avatar
cards — ready to present or export.

---

## Current status — M2 complete

| Milestone | Scope | State |
|---|---|---|
| **M1** | Solution + Angular scaffold, domain model, EF migration + seed, health check, CI | ✅ Done |
| **M2** | Board CRUD API + board editor wired to the live `SlideCanvas` | ✅ Done |
| M3 | Roster CRUD, typeahead, membership, composition | Planned |
| M4 | SignalR realtime, Present mode, PNG/PDF export, JSON import/export | Planned |
| M5 | JWT auth + RBAC, portfolio view, Jira sync, audit log | Planned |
| M6 | Test coverage, e2e, Dockerfiles, K8s/Helm, docs | Planned |

M2 delivers the working product loop: create a board, edit it in the builder, watch the
slide update live, save it. The roster screen is still a deliberate placeholder naming
the milestone that fills it, and squad membership is read-only until M3.

---

## Prerequisites

- **.NET 8 SDK** (the repo pins it via `global.json`)
- **Node 20.19+ / 22.12+ / 24.x** and npm
- **SQL Server** — SQL Server Express is fine; the default connection string points at
  `localhost\SQLEXPRESS`

## Run it locally

Two terminals.

**1. API** (listens on `http://localhost:5220`, Swagger at `/swagger`):

```bash
dotnet run --project src/Api/Api.csproj --launch-profile http
```

In `Development` this applies migrations and seeds the demo board automatically.

**2. Web** (listens on `http://localhost:4220`, proxies `/api` to the API):

```bash
npm start --prefix web
```

Then open <http://localhost:4220>.

### Verify the stack

```bash
curl http://localhost:5220/health
```

## Configuration

All settings are overridable by environment variable using the standard
`Section__Key` convention — no secrets belong in the repo.

| Key | Default | Purpose |
|---|---|---|
| `ConnectionStrings__Default` | `localhost\SQLEXPRESS` / `SquadStatusBoard` | Database connection |
| `Database__Provider` | `SqlServer` | `SqlServer` or `Postgres` |
| `Database__AutoMigrate` | `true` in Development, else `false` | Applies migrations on startup |
| `Database__SeedDemoData` | `true` in Development, else `false` | Seeds the demo board |
| `Cors__AllowedOrigins__0` | `http://localhost:4220` | Permitted web origin |
| `Jira__Enabled` | `false` | Gates the Jira integration |

Migrations are **never** applied automatically outside Development unless
`Database__AutoMigrate` is explicitly set, so a production deploy cannot silently
alter schema.

## Tests

```bash
dotnet test SquadStatusBoard.sln
```

```bash
npx ng test --watch=false --prefix web
```

## Database migrations

```bash
dotnet ef migrations add <Name> --project src/Infrastructure --startup-project src/Api --output-dir Persistence/Migrations
```

## Repository layout

```
/src
  /Domain           Entities, enums, value objects, domain rules — no dependencies
  /Application      DTOs, abstractions (IAppDbContext, IJiraClient, IExportRenderer)
  /Infrastructure   EF Core, migrations, seeder, integration implementations
  /Api              ASP.NET Core host, controllers, SignalR hub, auth
/web                Angular app (standalone components, Signals, strict mode)
/tests              Domain.Tests, Application.Tests (xUnit + FluentAssertions + SQLite)
/deploy             docker-compose, K8s manifests, Helm chart (M6)
/docs               ARCHITECTURE.md, API.md, SETUP.md
```

Dependency direction is `Domain ← Application ← Infrastructure/Api`. No EF Core types
appear in `Domain`.

## Known environment constraints

- **Docker is not installed on the development machine.** The Dockerfiles, compose file
  and Kubernetes/Helm manifests land in M6 as real deliverables but cannot be verified
  locally — they need validating in an environment that has a container runtime.
- **Server-side PDF export needs headless Chromium.** Until it is provisioned the API
  reports `serverExportEnabled: false` at `/api/v1/metadata/capabilities` and the client
  hides the affordance rather than offering a button that fails.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [API](docs/API.md)
- [Setup](docs/SETUP.md)
