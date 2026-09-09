# Squad Status Board

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
| **M7** | Delivery dashboard, portfolio charts, risk tracking | ✅ Done |
| **M8** | Clean install, Excel import/export, Jira linking | ✅ Done |
| **M9** | Jira settings screen, in-app guide, user management, configurable roles | ✅ Done |
| **M10** | Command-centre redesign, delivery analytics, board categories | ✅ Done |
| **M11** | Smartsheet integration alongside Jira | ✅ Done |
| **M12** | Staff scheduling: capacity, availability, work items, person profiles | ✅ Done |
| **M13** | In-app user manual covering the whole product | ✅ Done |

Every functional requirement in the spec is implemented and demoable end to end, plus a
delivery dashboard, risk tracking, Excel round-tripping and user administration on top.

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

In `Development` this applies migrations and creates the administrator automatically.

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

## Categories

**Categories** in the top nav (Admin only) groups boards into programmes — VIDA 4, AI,
and so on.

This is a level **above** a board's `Product`. Product names the module a board covers
(Discharge, Invoice, OPD UI); a category collects many of those under one programme:

```
Category   VIDA 4
  Board      Discharge Revamp        (Product: Discharge)
  Board      Admission Revamp        (Product: Admission)
Category   AI
  Board      Clinical Assistant      (Product: AI Assistant)
```

A board's category is **optional**. Boards created before categories existed keep working
and report as *Uncategorised* rather than being forced into a bucket nobody chose — and
the portfolio filter has an "Uncategorised" option so you can find them.

### Filing boards

Three ways, in ascending order of bulk:

| | How |
|---|---|
| One board | Set **Category** in the board editor |
| Many boards | `POST /api/v1/categories/assign` with a list of board ids |
| A whole portfolio | Fill the **Category** column in an Excel export and import it back |

The Excel column carries the category **name**, and a name that does not exist yet is
created on import — so an entire portfolio can be filed in one spreadsheet pass. Each
category created that way is reported in the import result, so a typo shows up as a new
category rather than disappearing silently.

### Retiring

Retiring is soft: the category leaves the pickers, but boards already in it keep their
grouping. Retiring a programme must never silently reshuffle the portfolio. Deleting a
category outright is not offered; if one were removed at the database level, its boards
fall back to uncategorised rather than being deleted with it.

## Roles

**Roles** in the top nav (Admin only) manages the values behind **Default role** on the
roster and on every squad.

Add one with a display name, a plural for the legend ("2 Scrum Masters"), and a colour —
the colour is the avatar and the composition-bar segment, so the form previews both. The
identifier is suggested from the display name and is what spreadsheets match on.

### What can and cannot change

| | Built-in seven | Roles you add |
|---|---|---|
| Rename and recolour | Yes | Yes |
| Change the identifier | No | No, once created |
| Retire | No | Yes |

The seven built-ins cannot be removed because every board and every exported spreadsheet
already stores their numbers. Renaming is enough: if your org says "Delivery Lead" rather
than "Tech Lead", say so and every screen follows.

**Retiring is soft.** The role leaves the pickers, but anyone already holding it keeps it
and still renders with the right label and colour — retiring a role must not blank out
last quarter's boards. The list shows how many people hold each role, so it is an
informed decision. Numbers are never reused, so a retired role's value cannot come back
attached to different people.

### How it works underneath

Roles a squad member holds are stored as numbers. The built-in seven keep the values 0–6
they have always had; roles you add are numbered from 100 up, so nothing already stored
had to be migrated and old spreadsheets still import.

Labels and colours are held in a process-wide catalogue, loaded at startup and refreshed
after every change, so adding a role applies without a restart. A role the catalogue does
not recognise renders as a neutral placeholder rather than throwing — worth knowing if you
run more than one API instance, where a role added on one is not known to the others until
they refresh. The role list itself always reads the database, so the pickers are never
stale.

## Users and access

**Users** in the top nav (Admin only) manages who can sign in. This is deliberately
separate from the **Roster**: the roster is who appears on a slide, users is who has
access. Most roster members never log in, and an admin need not be on any squad — an
account can optionally be linked to a roster person, but does not have to be.

| Access level | Can do |
|---|---|
| **Admin** | Everything: all boards, the roster, users, imports and Jira |
| **Product Owner** | Creates and edits their own boards; reads everyone else's |
| **Viewer** | Read, present and export. Cannot change anything |

### Adding someone

Add user → name, email, a password of at least 12 characters, and an access level. Give
them the password, and ask them to replace it: anyone signed in can change their own
password from **their name in the top-right corner**. That is what makes an admin-set
password acceptable — the admin only knows it until the person replaces it.

### Rules the server enforces

These live in the handlers, not the UI, so they hold however they are called:

- You cannot deactivate your own account, or change your own role. Ask another admin.
- The last active administrator cannot be deactivated or demoted.
- Deactivating someone, changing their role, or resetting their password **ends their
  session immediately** — an access token carries the old rights until it expires, so the
  refresh token is cleared rather than left to run out.
- Emails are unique and case-insensitive, so `P.Kumar@…` cannot become a second account
  for the same person.

Accounts are **deactivated, never deleted**. Boards and audit entries record who did what,
and deleting an account would leave that history pointing at nobody. Deactivated accounts
are hidden from the list until you tick *Show deactivated*.

## Staff scheduling

**Schedule** in the top nav shows everyone down the side and weeks across. Each cell is
**free capacity** — what that person still has available — because the question the page
exists to answer is "who can take this on".

### The capacity model

Simple enough to explain in a sentence, because a number nobody can explain is a number
nobody trusts:

```
available = 100 − whatever availability records take away
committed = the allocations on assignments running that week
free      = available − committed        (negative means over-committed)
```

Red means committed beyond capacity. Amber means fully away. The footer totals free
**people-weeks** across the team.

### Assignments become a schedule

A squad membership now carries an **allocation, a start and an end**. All three are
optional and open-ended is the norm — most people are on a squad until further notice, and
inventing an end date would put a commitment in the schedule nobody agreed to. An
assignment with no dates counts in every week; one with no allocation counts as **zero**,
and the page says how many are in that state so you know the total is understated rather
than wrong.

### Availability

Only **exceptions** are recorded: leave, public holidays, training, part-time. Anything not
listed is a normal full week. `CapacityPercent` is what *remains*, not what is lost — 0 is
fully away, 50 is half days — so it reads the same direction as an allocation. Where
periods overlap, the most restrictive one wins.

### Work items

Tasks on a board, optionally assigned. App-native rather than mirrored from Jira, because
most of what delays a revamp in your tracker is not a Jira issue at all — BRD sign-off, UAT
windows, ARB reviews, data migration — and everyone on the roster can be assigned one
whether or not they hold a Jira licence.

An unassigned task is a real state, and the one a lead scans for when filling capacity.
Completion stamps a date when the status becomes Done and **clears it if the task
reopens**, so "what did they finish last month" stays true.

You add them where the work is: **Tasks on this board**, in the board editor under the
squad. The assignee list is that board's own squad, because assigning work to somebody who
is not on the board is how a roster stops meaning anything. Status and assignee change
straight from the list — the two edits people make constantly. Viewers see the list and
not the controls.

### Person profile

Click any name. Shows their capacity this week, every assignment with its period, their
availability, open and completed work, and their recorded board changes.

The activity feed matches on the **display name** the audit trail recorded, because that is
what it has stored since before there was a roster to point at. Two people sharing a name
would merge, and a rename breaks the link — so the page prints how the match was made
rather than presenting a partial history as complete.

### Permissions

| Action | Who |
|---|---|
| View the schedule and profiles | Anyone signed in — knowing who has capacity is not privileged |
| Add and edit work items | Whoever can edit that board |
| Record availability | Admin, same gate as the roster |

## Connecting to your company's Jira

There are two ways to supply the credentials. **Most people should use the settings
screen**; the environment variables exist for locked-down deployments.

### 1. The settings screen (recommended)

Sign in as an Admin and open **Jira** in the top nav (`/settings/jira`).

| Field | What to put in it |
|---|---|
| **Jira URL** | Your Jira site, e.g. `https://yourcompany.atlassian.net`. Must be https (http is allowed only for a loopback address). |
| **Account email** | The Atlassian account the board reads as. |
| **API token** | Created at **id.atlassian.com → Security → API tokens** while signed in as that account. |

Use a **service account**, not your own login — when a person leaves the company their
token is revoked and every board would stop updating.

The account only needs **Browse projects** on the projects you want on the board. The
integration is read-only: it issues `GET /rest/api/3/search` and never writes to Jira.

Press **Test connection** with a project key to prove the credentials actually work. The
result distinguishes *not configured*, *configured but unreachable*, and *connected*, with
the issue count it read — because "no data" and "wrong token" look identical otherwise.

Then open each board and set its **Jira project key** (e.g. `PIRT`) in the builder. Boards
without a key are left alone.

#### How the token is stored

It is encrypted with ASP.NET Core Data Protection before it touches the database, under
its own named purpose. The API never sends it back to a browser — the screen shows only
`••••••••` plus the last four characters, which is enough to tell *which* token is in
place without exposing it. Leaving the token field blank on a later save keeps the stored
one, so an admin can change the interval without re-pasting the secret.

> The Data Protection key ring must be persisted. In a container without a mounted key
> directory the keys are regenerated on restart and the stored token can no longer be
> decrypted — the app logs this plainly and the fix is to re-enter the token.

### 2. Environment variables (pinned deployments)

```bash
setx Jira__Enabled true
setx Jira__BaseUrl https://yourcompany.atlassian.net
setx Jira__Email squad-board@yourcompany.com
setx Jira__ApiToken <token>
```

**Configuration wins over the settings screen.** When these are set, the API uses them and
the screen says so and goes read-only — so a hardened environment can pin the credentials
beyond the reach of an application admin.

### Automatic updates

Two modes, and the safe one is the default:

| Auto-apply | What happens |
|---|---|
| **Off** (default) | Jira only *suggests*. The board editor shows what Jira says and the Product Owner presses Save. Nothing is written unattended. |
| **On** | A background worker checks every *N* minutes and writes to every linked board on its own. |

A sync only ever sets **sprint, progress and status**. The blocker note, the risk level and
note, and the squad roster are written by people and are never overwritten — automation
that silently rewrites someone's commentary is worse than no automation.

Every change is recorded in the board's audit trail (as `Jira sync` for a scheduled run, or
under the admin's name for the **Sync now** button), and a board whose figures did not
change produces no audit entry and no realtime notification.

Changing the interval takes effect without restarting the API; the worker re-reads the
settings each minute.

## Connecting to Smartsheet

**Smartsheet** in the top nav (Admin only) works the same way as the Jira connection, with
two differences that come from the provider rather than from taste.

### 1. Get an access token

In Smartsheet: **Account → Personal Settings → API Access → Generate new access token**.

Use a **service account**, not your own login — when a person leaves, their token is
revoked and every linked board stops updating. The account only needs read access to the
sheets you want on the board. The integration never writes to Smartsheet.

Unlike Jira there is no account email to supply: Smartsheet authenticates with the bearer
token alone.

### 2. Tell it which columns to read

This is the real difference. A Jira issue has a universal shape — a status category the
API defines. A sheet is a spreadsheet, so it has no built-in notion of "done", and the
sync has to be told where to look:

| Setting | Default | What it reads |
|---|---|---|
| **Progress column** | `% Complete` | Averaged across rows. Accepts `0–1` fractions and `0–100` whole numbers |
| **Status column** | `Status` | Rows containing "blocked" count as blocked; "complete", "done" or "closed" count as finished |

The defaults match Smartsheet's own project templates. Progress prefers the percentage
column and only falls back to counting finished rows — the board says which was used, so a
Product Owner can tell a measured number from an inferred one.

### 3. Link each board

Open a board and paste its **sheet id** (in Smartsheet: **File → Properties → Sheet ID**).
Boards without one are ignored by every sync.

### What a sync changes

**Progress and status only.** Sprint is never touched: a sheet does not carry one, and
blanking a Product Owner's value with information the integration never had would lose
data rather than update it. Blocker notes, risk and the squad roster are left alone too,
exactly as with Jira.

Auto-apply is off by default. With it off Smartsheet only *suggests* in the board editor;
with it on, a background job writes to linked boards on the interval you set. Both
providers run on their own schedules and last-run times, so switching one off says nothing
about the other.

### Storage

Identical to Jira: the token is encrypted with ASP.NET Core Data Protection under its own
key purpose, never returned to a browser, and shown only as a mask plus the last four
characters. A blank token field on a later save keeps the stored one. `Smartsheet__Enabled`
and `Smartsheet__AccessToken` pin the connection from the environment and override the
screen, which then goes read-only and says so.

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
| `Jira__Enabled` | `false` | Pins Jira on via configuration, overriding the settings screen |
| `Jira__BaseUrl` | — | e.g. `https://you.atlassian.net` |
| `Jira__Email` / `Jira__ApiToken` | — | Jira Cloud credentials. Set these only to pin them; otherwise use the settings screen |
| `Cors__AllowedOrigins__0` | `http://localhost:4220` | Permitted web origin |

Migrations are **never** applied automatically outside Development unless
`Database__AutoMigrate` is explicitly set, so a production deploy cannot silently
alter schema.

## Tests

Backend — 102 tests (domain rules, handlers, RBAC, Excel and JSON portability):

```bash
dotnet test SquadStatusBoard.sln
```

Frontend — 20 component and shell tests:

```bash
npx ng test --watch=false --prefix web
```

End-to-end — 9 Playwright tests; 2 skip on a clean install because they need the demo
role accounts. **Both servers must already be running.**

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

End users don't need any of the above. The manual ships **inside the app** at `/help`
(**User manual** in the nav, open to everyone signed in, including Viewers) and covers the
whole product: boards, roster, tasks, the capacity model, programmes, reporting, presenting,
the Excel and JSON round trip, the integrations, accounts, and what to check when something
looks wrong. It prints cleanly, so it doubles as a handover document.

The Jira sync guide is one chapter of it, at `/help/jira-sync` — reached from the manual,
from the Jira settings screen, and from the suggestion panel on a board.
