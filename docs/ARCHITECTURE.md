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
