# API

Base path `/api/v1`. JSON in, JSON out. Validation failures and domain-rule violations
return RFC 7807 `application/problem+json`.

## Implemented in M1

### `GET /api/v1/metadata`

Role and status reference data with canonical labels and design-token colours. The client
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

### Boards — M2

```
GET    /api/v1/boards                  list (paginated portfolio)
POST   /api/v1/boards                  create
GET    /api/v1/boards/{id}             board + members, composition computed
PUT    /api/v1/boards/{id}             update meta
POST   /api/v1/boards/{id}/duplicate   deep copy including membership
DELETE /api/v1/boards/{id}             soft delete
PUT    /api/v1/boards/reorder          [{ id, orderIndex }]
```

### Membership and roster — M3

```
GET    /api/v1/boards/{id}/members
POST   /api/v1/boards/{id}/members     { personId | inline person, role, detail, allocation }
PUT    /api/v1/members/{id}
DELETE /api/v1/members/{id}

GET    /api/v1/people                  roster, typeahead via ?q=
POST   /api/v1/people
PUT    /api/v1/people/{id}
DELETE /api/v1/people/{id}             soft delete (IsActive = false)
```

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
