# Architecture

## Layering

```
Domain  ←  Application  ←  Infrastructure
                       ←  Api
```

The arrow points at the dependency. `Domain` references nothing; `Application` references
only `Domain`; `Infrastructure` and `Api` reference `Application`. `Api` also references
`Infrastructure` solely to register services in the composition root.

### Domain

Entities (`Board`, `Person`, `SquadMember`, `BoardAuditEntry`), enums, and the
`SquadComposition` value object. Invariants are enforced in constructors and mutation
methods, which throw `DomainException`; there are no public setters, so an entity cannot
be put into an invalid state by assignment.

Two decisions worth calling out:

- **Role colours and status colours live in the Domain** (`RoleMetadata`,
  `BoardStatusMetadata`) rather than only in CSS. The server renders exports, so it needs
  the palette. The client fetches these values from `/api/v1/metadata` instead of
  hardcoding them, which leaves exactly one authority for the design tokens.
- **Composition is derived, never stored.** `Board.Composition` recomputes from current
  membership on every read, so counts cannot drift from reality.

### Application

DTOs (the wire contract), and the abstractions the outer layers implement:
`IAppDbContext`, `ICurrentUser`, `IBoardNotifier`, `IJiraClient`, `IExportRenderer`.
Keeping SignalR, Jira and Chromium behind interfaces is what lets the use-cases be tested
without any of them.

### Infrastructure

EF Core context, entity configurations, migrations, the seeder, and integration
implementations. The database provider is selected at runtime from `Database:Provider`,
so the same build runs on SQL Server or PostgreSQL.

### Api

The ASP.NET Core host: controllers, the SignalR hub, auth, Serilog, CORS, health checks,
and RFC 7807 problem details.

## Soft deletion

Both `Board` and `Person` are soft-deleted, for different reasons and by different
mechanisms.

- **Boards** carry `IsDeleted` and a global EF query filter. `SquadMember` carries a
  matching filter (`!m.Board.IsDeleted`) so that querying members directly — which the
  `PUT /members/{id}` endpoint does — cannot reach members of a deleted board.
- **People** carry `IsActive` and deliberately have **no** query filter, because the
  roster manager must be able to list deactivated people in order to reactivate them.
  The `SquadMember → Person` foreign key uses `DeleteBehavior.Restrict` so deactivating
  somebody can never cascade away the historical record of squads they were on.

`BoardAuditEntry` has no foreign key to `Boards` on purpose: audit rows must outlive the
board they describe.

## Derived values

`SquadComposition.From` groups assignments by role, orders them by the canonical display
order, and computes each segment's percentage. Rounding drift is pushed onto the largest
segment so the composition bar always fills exactly 100% — otherwise a three-way split
would leave a visible sliver of background at the end of the bar.

## Configuration and secrets

Everything is read from `IConfiguration`, so any setting can be overridden by an
environment variable (`Section__Key`) or a Kubernetes secret. Nothing sensitive is
committed. Migrations run on startup only when `Database:AutoMigrate` is true, which
defaults to true in Development and false everywhere else.

## Fidelity to the prototype

`docs/prototype/squad-status-board.html` is the visual acceptance baseline. `SlideCanvas`
and its sub-components are a transcription of it, verified by diffing computed styles
between the two in a browser: slide radius and padding, eyebrow and product chip, the
29px display title, the 150px ring with its 13px band, the 12px composition bar on a
`--panel-2` track with 2px segment gaps, the "The squad" rule, the auto-fill member grid,
the 36px rounded-square avatars, and the footer all match.

Two deliberate departures:

- **Pluralisation.** The prototype appends `"s"` to the role label, which produces
  "DevOpss". The server supplies a correct `pluralLabel` per role instead, so the legend
  reads "1 DevOps". Fixing this in the Domain keeps client and export in step.
- **Composition widths.** The prototype sizes bar segments with `flex: count`. The server
  computes exact percentages that are guaranteed to sum to 100, which renders identically
  but also lets an export or another API consumer reproduce the bar without re-deriving it.

The slide reflows to its container exactly as the prototype does. An earlier revision
scaled a fixed 1280x720 canvas; that was dropped once the prototype confirmed a fluid
layout. Export fidelity is handled by rendering at a fixed viewport width in the export
path (M4) rather than by constraining the component.

## Membership and the roster

`SquadMember` is the join between a `Board` and a `Person`, and it carries its **own**
role. A Tech Lead on the roster can be a Developer on one squad without the roster
default changing — the assignment is the authority for what the slide shows.

Adding a member goes through `Board.AddMember`, which enforces the duplicate and
"inactive person" rules on the aggregate, so both the roster-pick and quick-create paths
are covered by the same invariants. The handler then tracks the new `SquadMember`
explicitly with `db.SquadMembers.Add(member)`: `Entity` assigns its own GUID in the
constructor, and EF Core marks an untracked entity reached through a tracked parent's
navigation as `Modified` rather than `Added` when its key is already set — which emits an
UPDATE against a row that does not exist yet.

## Realtime

Handlers publish through `IBoardNotifier`; `SignalRBoardNotifier` in the API layer is the
only code that knows the transport exists. Infrastructure registers a no-op notifier and
the API composition root replaces it, so the Application layer and its tests never take a
SignalR dependency.

Viewers join a per-board group (`board:{id}`), so an edit reaches only the people looking
at that board. A broadcast failure is caught and logged rather than propagated: a
notification problem must never roll back a write that already succeeded.

On the client, an incoming event bumps a revision counter and the editor refetches. The
refetch reads `isDirty()` **before** replacing the server board — comparing the draft
against already-updated server state would make a clean editor look dirty and silently
discard the incoming values. Unsaved local edits always win over a broadcast; the server
state updates underneath them.

## Export

`/slide/{id}` and `/slide/all` render the slide outside the app shell. The headless
renderer loads those routes, so an export goes through the same `SlideCanvas` component
the user sees — there is no parallel slide implementation to keep in step.

The capture is clipped to the slide element rather than the viewport, because the slide is
content-height: a fixed-height capture pads a short squad with dead space. The portfolio
PDF renders `/slide/all`, which stacks every slide with a CSS page break, so Chromium
paginates in one pass and no PDF-merging dependency is needed.

**The slide deliberately avoids `color-mix()`.** It computes to `color(srgb …)`, which
html2canvas cannot parse — client-side PNG export failed with "Attempting to parse an
unsupported color function". Tints are computed to plain `rgba()` in `shared/slide/color.ts`
instead, which is also what the prototype did by appending an alpha suffix to the hex.
Keep new slide styles free of `color-mix`, `oklch` and other modern colour functions.

## Auth and RBAC

`AppUser` is deliberately separate from `Person`: the roster is who appears on slides,
`AppUser` is who has access. Most roster members never sign in, and an admin need not be
on any squad.

Access tokens are short-lived JWTs carrying the role, so most authorisation needs no
database round trip. Refresh tokens are opaque random strings — never JWTs — stored only
as a SHA-256 hash and **rotated on every use**, so a stolen refresh token is good only
until the legitimate client next refreshes. Login returns one message for "no such user",
"wrong password" and "deactivated", because telling them apart lets an attacker enumerate
accounts.

Authorisation is enforced **in the handlers**, not only on routes:

- A role claim answers "may this user write at all".
- `IBoardAuthorizer` answers "may this user write *this* board", which needs the board
  loaded and so cannot live in an attribute.

The Angular route guards mirror these rules but are a convenience only — every rule is
re-checked server-side, so bypassing the router gains nothing. `RbacTests` asserts on the
handlers rather than the endpoints for exactly this reason.

The authorization fallback policy requires an authenticated user, so a newly added
controller is secure by default and must opt out with `[AllowAnonymous]` rather than
accidentally shipping public.

### Board ownership

`Board.OwnerId` is set to the creator. A Product Owner may write only their own boards;
an Admin may write any. **Ownerless boards are admin-only** — that covers seeded and
imported boards, and any board created before ownership existed. The seeder adopts the
one known demo board so the Product Owner path is demonstrable, but deliberately leaves
other pre-existing boards ownerless: guessing an owner for somebody else's board is worse
than requiring an admin to assign one.

## Jira

Config-gated and read-only. `GetJiraSuggestionQuery` is a **query, not a command** — it
never writes to the board. It returns the pulled numbers, a suggested progress and status,
and a plain-English rationale; the Product Owner applies them to the draft and still has
to press Save. When Jira is not configured the null-object client reports `IsEnabled =
false`, `/metadata/capabilities` says so, and the UI hides the affordance rather than
offering a button that fails.
