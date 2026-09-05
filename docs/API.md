# API

Base path `/api/v1`. JSON in, JSON out. Validation failures and domain-rule violations
return RFC 7807 `application/problem+json`.

## Implemented

### Boards (M2)

```
GET    /api/v1/boards                  list (paginated portfolio)
POST   /api/v1/boards                  create
GET    /api/v1/boards/{id}             board + members, composition computed
PUT    /api/v1/boards/{id}             update meta
POST   /api/v1/boards/{id}/duplicate   deep copy including membership
DELETE /api/v1/boards/{id}             soft delete
PUT    /api/v1/boards/reorder          [{ id, orderIndex }]
```

`GET /boards` accepts `?q=` (matches title, product or squad), `?status=` (integer enum),
`?page=` and `?pageSize=`.

`GET /boards/{id}` returns the board with its members, the **server-computed**
composition, and advisory `warnings`:

```json
{
  "id": "8f1c4d10-0000-4000-a000-000000000001",
  "title": "OPD Screen Revamp",
  "product": "VIDA HIS",
  "squadName": "Squad Alpha",
  "sprint": "Sprint 14",
  "status": 0,
  "statusLabel": "On Track",
  "statusColor": "#34D399",
  "progressPercent": 68,
  "composition": {
    "total": 6,
    "legendText": "1 Product Owner · 1 Tech Lead · 2 Developers · 1 QA Engineer · 1 UI/UX Designer",
    "segments": [
      { "role": 0, "label": "Product Owner", "pluralLabel": "Product Owners",
        "color": "#2DD4BF", "count": 1, "percent": 16.67 }
    ]
  },
  "members": [
    {
      "id": "…",
      "fullName": "Nadia Al-Harbi",
      "initials": "NA",
      "role": 0,
      "roleLabel": "Product Owner",
      "roleColor": "#2DD4BF",
      "detail": "Outpatient journey · KPI owner",
      "orderIndex": 0
    }
  ],
  "warnings": []
}
```

Segment percentages always sum to exactly 100 — rounding drift is absorbed by the
largest segment so the composition bar fills completely.

`warnings` are advisory and never block a write: a squad with no Product Owner or no
Developers, or a Blocked board with no blocker note, still saves.

Validation failures return every field's problem at once:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Validation failed",
  "status": 400,
  "instance": "/api/v1/boards",
  "errors": {
    "Title": ["'Title' must not be empty."],
    "ProgressPercent": ["'Progress Percent' must be between 0 and 100. You entered 150."]
  }
}
```

### Membership and roster (M3)

```
GET    /api/v1/boards/{id}/members
POST   /api/v1/boards/{id}/members         { personId | newPerson, role, detail, allocationPercent }
PUT    /api/v1/members/{id}                { role, detail, allocationPercent }
DELETE /api/v1/members/{id}
PUT    /api/v1/boards/{id}/members/reorder [memberId, ...]

GET    /api/v1/people                      roster; ?q= typeahead, ?includeInactive=true
POST   /api/v1/people
PUT    /api/v1/people/{id}
DELETE /api/v1/people/{id}                 soft delete (IsActive = false)
POST   /api/v1/people/{id}/reactivate
```

Adding a member takes **either** `personId` (an existing roster entry) **or** `newPerson`
(quick-creates them, so the name joins the roster and is reusable) — never both.

A member carries its own `role`, which may differ from the person's `defaultRole`; changing
it never edits the roster. `?q=` matches name, email and default detail.

Deleting a person is always soft: they leave the picker but every `SquadMember` row they
appear in survives, so historical boards still show who delivered them.

### Realtime, export and bulk data (M4)

```
GET    /api/v1/boards/{id}/export/png    2x PNG of the slide
GET    /api/v1/boards/{id}/export/pdf    single-slide PDF
GET    /api/v1/portfolio/export/pdf      every board, one slide per page
GET    /api/v1/export                    bulk JSON download
POST   /api/v1/import                    bulk JSON restore
```

SignalR hub at `/hubs/boards`. Clients call `JoinBoard(boardId)` / `LeaveBoard(boardId)`
and receive `BoardUpdated` and `MemberChanged` for the board they joined — updates are
scoped to a per-board group rather than broadcast to everyone connected.

The render endpoints load the web app's own `/slide/{id}` route in headless Chromium, so
exports go through the same `SlideCanvas` the user sees; there is no second implementation
of the slide to keep in step. The capture is clipped to the slide element, so a short
squad does not produce an image padded with empty space.

Export is **off by default** and returns `503` with a remediation message when no renderer
is configured. `/api/v1/metadata/capabilities` reports `serverExportEnabled` so the client
can hide the affordance instead of offering a button that fails.

`GET /export` returns a versioned file; `POST /import` accepts it back and **upserts by
id**, so importing the same file twice changes nothing the second time:

```json
{ "peopleCreated": 0, "peopleUpdated": 9, "boardsCreated": 0,
  "boardsUpdated": 2, "membersLinked": 6, "warnings": [] }
```

A member whose person is missing from both the file and the database is skipped with a
warning rather than failing the whole import. Deactivated people round-trip as inactive,
and an imported board keeps members who have since left — history is not rewritten.

### Metadata (M1)

`GET /api/v1/metadata` — role and status reference data with canonical labels and design-token colours. The client
fetches this at startup so the palette is defined in one place.

```json
{
  "roles": [
    { "value": 0, "name": "ProductOwner", "label": "Product Owner", "color": "#2DD4BF" },
    { "value": 1, "name": "TechLead", "label": "Tech Lead", "color": "#A78BFA" },
    { "value": 2, "name": "Developer", "label": "Developer", "color": "#6366F1" },
    { "value": 3, "name": "QaEngineer", "label": "QA Engineer", "color": "#F59E0B" },
    { "value": 4, "name": "UxDesigner", "label": "UI/UX Designer", "color": "#EC4899" },
    { "value": 5, "name": "BusinessAnalyst", "label": "Business Analyst", "color": "#38BDF8" },
    { "value": 6, "name": "DevOps", "label": "DevOps", "color": "#10B981" }
  ],
  "statuses": [
    { "value": 0, "name": "OnTrack", "label": "On Track", "color": "#34D399" },
    { "value": 1, "name": "AtRisk", "label": "At Risk", "color": "#FBBF24" },
    { "value": 2, "name": "Blocked", "label": "Blocked", "color": "#F87171" },
    { "value": 3, "name": "InReview", "label": "In Review", "color": "#60A5FA" },
    { "value": 4, "name": "Delivered", "label": "Delivered", "color": "#2DD4BF" }
  ]
}
```

### `GET /api/v1/metadata/capabilities`

Which optional integrations this deployment actually has. The client hides affordances
that would fail rather than offering buttons that error.

```json
{ "jiraSyncEnabled": false, "serverExportEnabled": false }
```

### `GET /health`

Liveness and database connectivity. Returns `200 Healthy` or `503 Unhealthy`.

---

### Roles (M9)

The values behind "Default role". Reading is open to anyone signed in — every picker needs
the list. Writing is **Admin only**, enforced in the handlers.

```
GET  /api/v1/roles          ?includeInactive=
POST /api/v1/roles
PUT  /api/v1/roles/{value}
PUT  /api/v1/roles/{value}/active
```

`value` is the number stored on every `SquadMember` and `Person`. The built-in seven keep
**0–6**, the values they have always had, so nothing already stored needed migrating.
Roles added by an admin are numbered from **100** up, and numbers are never reused.

- The built-ins can be renamed and recoloured but **not retired**, and their `name` is
  fixed — it is what spreadsheets match on.
- `name` must be a plain word (`^[A-Za-z][A-Za-z0-9]*$`); `color` must be `#RRGGBB`.
- Retiring is soft: the role leaves `/metadata`, but people already holding it keep it and
  still render with their label and colour.

`GET /api/v1/metadata` reads the database directly and returns only **active** roles, so
pickers are never stale. Role assignment is validated against the catalogue rather than the
enum, so custom roles are accepted and retired ones remain editable.

### Users and access (M9)

Accounts that can sign in — distinct from the roster, which is who appears on slides.

```
GET    /api/v1/users            ?q=&includeInactive=&page=&pageSize=
GET    /api/v1/users/roles
POST   /api/v1/users
PUT    /api/v1/users/{id}
PUT    /api/v1/users/{id}/active
PUT    /api/v1/users/{id}/password
PUT    /api/v1/users/me/password
```

**Admin-only except `PUT me/password`**, which any signed-in user may call — that is what
makes an admin-set password acceptable, since the owner can replace it. Authorisation is
enforced in the handlers, not on the routes.

A password is only ever accepted, never returned: `UserDto` carries `hasPassword`, never a
hash. Passwords must be at least 12 characters.

Rules the API enforces, each returning `400` with a readable `detail`:

- You cannot deactivate your own account or change your own role (`PUT {id}` / `{id}/active`).
- The last active administrator cannot be deactivated or demoted.
- Emails are unique, compared case-insensitively.

Deactivating a user, changing their role, or resetting their password **clears their
refresh token**, ending the session rather than letting the old access token run to expiry.

Accounts are deactivated, never deleted — boards and audit entries reference who did what.

### Jira integration (M5, M8)

Administering the connection. **Every route here is Admin-only** — they read and write a
credential that acts on behalf of the whole organisation.

```
GET    /api/v1/integrations/jira
PUT    /api/v1/integrations/jira
DELETE /api/v1/integrations/jira
POST   /api/v1/integrations/jira/test
POST   /api/v1/integrations/jira/sync
POST   /api/v1/boards/{id}/jira/sync
```

`GET` returns the connection with the token **masked** — `tokenHint` is `••••••••` plus the
last four characters. The plaintext token is never serialised to a client.

`PUT` saves it. An **empty `apiToken` means "keep the stored one"**: the client is never
given the token, so it cannot send it back, and a blank field must not wipe a working
connection. The URL must be https (http is accepted only for a loopback host).

`POST .../test` makes a real call, so *not configured*, *unreachable* and *connected* can be
told apart. Pass `{ "projectKey": "ABC" }` to check the account can actually read a project.

`POST .../sync` runs the sync across every board with a project key and returns
`{ ran, message, boardsConsidered, boardsUpdated, boardsUnreachable, details }`. Pressing it
is an explicit instruction, so it writes even when auto-apply is off; the scheduled run
respects that switch. Changes land in each board's audit trail.

`POST /boards/{id}/jira/sync` returns a **suggestion** for one board and never writes.

When `Jira__ApiToken` is set in configuration it overrides anything saved here, and `GET`
reports `overriddenByConfiguration: true`.

## Conventions

- **Pagination** — list endpoints take `?page=` and `?pageSize=` (default 50, max 200)
  and return `{ items, page, pageSize, totalCount, totalPages, hasNext, hasPrevious }`.
- **Errors** — `400` for domain-rule violations and validation, `404` for missing
  resources, `500` with no exception detail leaked to the caller.
- **Enums** — serialised as their integer values; `name` and `label` come from
  `/metadata`. Values are stable and must not be renumbered.
