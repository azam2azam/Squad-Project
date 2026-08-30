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

## Planned

Signatures are fixed; the implementations arrive in the milestone noted.

### Realtime, export and bulk data — M4

```
GET    /api/v1/boards/{id}/export/png
GET    /api/v1/boards/{id}/export/pdf
GET    /api/v1/portfolio/export/pdf
POST   /api/v1/import                  bulk import boards + roster
GET    /api/v1/export                  bulk export
```

SignalR hub at `/hubs/boards`: client calls `JoinBoard(boardId)`; the server raises
`BoardUpdated` and `MemberChanged`.

### Jira — M5

```
POST   /api/v1/boards/{id}/jira/sync
```

Returns the pulled sprint name, done-vs-total ratio and a suggested progress/status.
It never writes to the board — the Product Owner reviews and accepts.

## Conventions

- **Pagination** — list endpoints take `?page=` and `?pageSize=` (default 50, max 200)
  and return `{ items, page, pageSize, totalCount, totalPages, hasNext, hasPrevious }`.
- **Errors** — `400` for domain-rule violations and validation, `404` for missing
  resources, `500` with no exception detail leaked to the caller.
- **Enums** — serialised as their integer values; `name` and `label` come from
  `/metadata`. Values are stable and must not be renumbered.
