| **M6** | e2e tests, Dockerfiles, K8s/Helm, docs | ✅ Done || **M7** | Delivery dashboard, portfolio charts, risk tracking | ✅ Done |# Squad Status Board

A production web application for composing, maintaining and presenting live status
snapshots for engineering squads inside the **Product Innovation & Revamp Team (PIRT)**.

A delivery leader fills in a builder form on the left and a boardroom-quality dark slide
renders live on the right — progress ring, squad-composition bar, role-coloured avatar
cards — ready to present or export.

---

## Current status — complete, plus a delivery dashboard

| Milestone | Scope | State |
|---|---|---|
| **M1** | Solution + Angular scaffold, domain model, EF migration + seed, health check, CI | ✅ Done |
| **M2** | Board CRUD API + board editor wired to the live `SlideCanvas` | ✅ Done |
| **M3** | Roster CRUD, typeahead, membership, composition | ✅ Done |
| **M4** | SignalR realtime, Present mode, PNG/PDF export, JSON import/export | ✅ Done |
| **M5** | JWT auth + RBAC, Jira sync, audit log | ✅ Done |
| **M6** | e2e tests, Dockerfiles, K8s/Helm, docs | ✅ Done |

All six milestones are done. Every functional requirement in the spec is implemented and
demoable end to end.

Sign in with one of the seeded demo accounts — password `Demo!Pass123`:

| Account | Role | Can do |
|---|---|---|
| `admin@pirt.example` | Admin | Everything, including the roster and imports |
| `po@pirt.example` | Product Owner | Full control of boards they own; reads the rest |
| `viewer@pirt.example` | Viewer | Read, present and export only |

The reference prototype is checked in at [docs/prototype/squad-status-board.html](docs/prototype/squad-status-board.html)
 and the `SlideCanvas` is a deliberate transcription of it — see [Architecture](docs/ARCHITECTURE.md)
 for the two places it intentionally differs.

---

## First run

The application starts **empty** — no example boards, no roster. One administrator is
created so you can sign in:

| | |
|---|---|
| Email | `admin@pirt.example` |
| Password | `Admin!Pass123` (or whatever `Database__AdminPassword` is set to) |

**Change that password.** It comes from configuration, not a secret store. Set your own
before first run:

```bash
setx Database__AdminPassword "something-only-you-know"
```

The administrator is only created when the database has **no users at all**, so it never
resets a password you have already changed.

Want the example content back? Set `Database__SeedDemoData=true` and restart — that adds
the OPD Screen Revamp board, a nine-person roster, and `po@` / `viewer@` accounts for
demonstrating the roles.

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

## Excel import and export

Every board, person and squad assignment round-trips through a spreadsheet, so the
portfolio can be edited in bulk or handed to somebody who does not use the app.

- **Export Excel** on the Boards screen downloads a workbook with four sheets:
  `Boards`, `People`, `Members`, and a `Read me` explaining how to edit it.
- **Import Excel** reads an edited workbook back.

Rows are matched on **Id**, so editing a row updates it rather than creating a duplicate,
and importing the same file twice changes nothing the second time. Leave `Id` blank on a
new row and one is generated. Enum columns are written as labels ("At Risk", "Critical"),
and reading accepts the label, the name, or the number.

A bad cell is refused with the sheet, row and column named — for example
*"Boards!Progress % on row 4 is 150. It must be between 0 and 100."* — rather than a
generic failure, because that is the only way to fix the file.

Removing a row from `Members` takes that person off the squad. Removing a **board** row
does not delete the board; delete it in the app so the audit trail is kept.

## Linking a board to Jira

Each board has **Jira project key** and **Jira board id** fields in the builder. These can
be filled in whether or not sync is switched on, so boards can be prepared in advance.

To enable the integration, set these and restart the API:

```bash
setx Jira__Enabled true
```

`Jira__BaseUrl` (e.g. `https://yourcompany.atlassian.net`), `Jira__Email`, and
`Jira__ApiToken` (from id.atlassian.com → Security → API tokens) are also required.

Check it worked — admin only, because it spends your credentials:

```bash
curl -H "Authorization: Bearer <token>" "http://localhost:5220/api/v1/metadata/jira/connection?projectKey=ABC"
```

It reports one of three things: not configured, configured but unreachable, or connected
with the issue count it read. That distinction matters — "no data" and "wrong token" look
identical otherwise.

Once enabled, a **Sync from Jira** button appears on any board with a project key. It
pulls the active sprint, the done-vs-total ratio and a blocked count, then shows a
**suggestion**. It never writes to the board: applying it fills the form, and you still
press Save.

## Configuration

All settings are overridable by environment variable using the standard
`Section__Key` convention — no secrets belong in the repo.

| Key | Default | Purpose |
|---|---|---|
| `ConnectionStrings__Default` | `localhost\SQLEXPRESS` / `SquadStatusBoard` | Database connection |
| `Database__Provider` | `SqlServer` | `SqlServer` or `Postgres` |
| `Database__AutoMigrate` | `true` in Development, else `false` | Applies migrations on startup |
| `Database__SeedAdminUser` | `true` | Creates an admin when the database has no users at all |
| `Database__AdminEmail` | `admin@pirt.example` | Initial administrator |
| `Database__AdminPassword` | `Admin!Pass123` | **Change this.** Only used on first run |
| `Database__SeedDemoData` | `false` | Example boards, roster and the extra role accounts |
| `Jira__Enabled` | `false` | Turns on Jira sync |
| `Jira__BaseUrl` | — | e.g. `https://you.atlassian.net` |
| `Jira__Email` / `Jira__ApiToken` | — | Jira Cloud credentials |
| `Cors__AllowedOrigins__0` | `http://localhost:4220` | Permitted web origin |
| `Jira__Enabled` | `false` | Gates the Jira integration |

Migrations are **never** applied automatically outside Development unless
`Database__AutoMigrate` is explicitly set, so a production deploy cannot silently
alter schema.

## Tests

Backend — 80 tests (domain rules, handlers, RBAC, import/export):

```bash
dotnet test SquadStatusBoard.sln
```

Frontend — 20 component and shell tests:

```bash
npx ng test --watch=false --prefix web
```

End-to-end — 9 Playwright tests (2 skip on a clean install: they need the demo role accounts). **Both servers must already be running.**

```bash
npm run e2e --prefix web
```

Locally these drive your installed Chrome, because this machine's security software
blocks Playwright's own downloaded browsers from launching. CI uses the pinned build.

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

- **Docker, kubectl and helm are not installed on the development machine.** The
  Dockerfiles, compose file, Kubernetes manifests and Helm chart are written to the spec
  but have never been run. See [Deployment](docs/DEPLOYMENT.md) — validate them on a host
  with a container runtime before trusting them.
- **Server-side export needs a headless browser and is off by default.** Set
  `Export__Enabled=true`. On this machine PuppeteerSharp's downloaded Chromium would not
  launch (blocked by the host's security software), so point `Export__ChromiumPath` at an
  installed browser instead:

  ```bash
  setx Export__ChromiumPath "C:\Program Files\Google\Chrome\Application\chrome.exe"
  ```

  With that set, PNG and PDF render correctly at 2x. While it is off the API reports
  `serverExportEnabled: false` at `/api/v1/metadata/capabilities` and the client hides the
  PDF button rather than offering something that fails — client-side PNG always works.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [API](docs/API.md)
- [Setup](docs/SETUP.md)
- [Deployment](docs/DEPLOYMENT.md)
