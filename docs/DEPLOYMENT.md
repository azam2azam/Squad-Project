# Deployment

> **Not verified locally.** Docker, `kubectl` and `helm` are not installed on the machine
> this was written on. The Dockerfiles, compose file, manifests and chart are real
> deliverables written to the spec, but they have not been run. Validate them on a host
> with a container runtime before trusting them — start with `docker compose`, then
> `helm lint` and `helm template … | kubectl apply --dry-run=client -f -`.

## Local, with containers

```bash
docker compose -f deploy/docker-compose.yml up --build
```

Brings up SQL Server, the API and the web app. Open <http://localhost:8080> and sign in
as `admin@pirt.example` / `Demo!Pass123`.

The compose file waits on a SQL Server healthcheck before starting the API, so the first
run does not fail migrating against a database that is still booting.

## Images

| Image | Dockerfile | Notes |
|---|---|---|
| API | `src/Api/Dockerfile` | Includes Chromium for server-side export, so the first export is not a several-minute download. Runs as uid 64198. |
| Web | `web/Dockerfile` | nginx serving the built Angular app; proxies `/api`, `/health` and `/hubs` to the API service so the browser makes same-origin requests. |

Both build from the **repository root**:

```bash
docker build -f src/Api/Dockerfile -t squad-status-board-api .
```

## Kubernetes

```bash
kubectl apply -f deploy/k8s/namespace.yaml
kubectl apply -f deploy/k8s/configmap.yaml
kubectl apply -f deploy/k8s/api.yaml -f deploy/k8s/web.yaml
```

Secrets are **not** in a manifest. Create them out of band:

```bash
kubectl -n squad-status-board create secret generic api-secrets --from-literal=Jwt__SigningKey="$(openssl rand -base64 48)" --from-literal=ConnectionStrings__Default='Server=...;Database=SquadStatusBoard;...'
```

`deploy/k8s/secret.example.yaml` is a shape reference only. In a real cluster prefer an
External Secrets Operator or sealed-secrets so values never sit in a file.

### Migrations

`Database__AutoMigrate` is **false** in the Deployment. Migrations run as a separate Job
(a Helm `pre-upgrade` hook), because otherwise every replica races to alter the same
schema on rollout. The Job sets `RunMigrationsAndExit=true`, which makes the API apply
migrations and exit rather than start a web server.

## Helm

```bash
helm lint deploy/helm
helm template ssb deploy/helm | kubectl apply --dry-run=client -f -
helm upgrade --install ssb deploy/helm --namespace squad-status-board --create-namespace
```

Values worth setting per environment:

| Value | Purpose |
|---|---|
| `image.registry` / `image.*.tag` | Where the images come from |
| `config.corsOrigin` | Must match the public host, or the browser blocks the API |
| `config.jwtAuthority` | Set to an OIDC issuer to federate instead of local tokens |
| `config.export.enabled` | Server-side PNG/PDF; needs Chromium in the image |
| `existingSecret` | Name of the pre-created secret holding the connection string and signing key |

The API Deployment's pod template carries a checksum of the ConfigMap, so a config change
rolls the pods — a ConfigMap update alone would not.

### Ingress

One backend only: the web image's nginx already proxies `/api` and `/hubs` to the API
Service, so routing them at the Ingress as well would be a second place to keep in step.

SignalR needs long-lived connections, hence:

```yaml
nginx.ingress.kubernetes.io/proxy-read-timeout: "3600"
```

Without it the default 60s timeout drops the WebSocket and the client reconnect-loops.

## Configuration reference

Everything is read from `IConfiguration`, so any setting is overridable by environment
variable using `Section__Key`.

| Key | Default | Purpose |
|---|---|---|
| `ConnectionStrings__Default` | SQL Express | Database connection |
| `Database__Provider` | `SqlServer` | `SqlServer` or `Postgres` |
| `Database__AutoMigrate` | dev only | Applies migrations on startup |
| `Database__SeedDemoData` | dev only | Seeds the demo board and accounts |
| `RunMigrationsAndExit` | `false` | Migrate, then exit — for the migration Job |
| `Jwt__SigningKey` | *(none)* | **Required.** ≥32 chars. Startup fails without it |
| `Jwt__Authority` | *(empty)* | Set to federate with OIDC |
| `Cors__AllowedOrigins__0` | `http://localhost:4220` | Permitted web origin |
| `Export__Enabled` | `false` | Server-side PNG/PDF |
| `Export__ChromiumPath` | *(empty)* | Path to an installed browser |
| `Jira__Enabled` | `false` | Gates the Jira integration |

No secret has a default. `Jwt__SigningKey` deliberately throws at startup when missing or
too short, rather than silently signing tokens with a weak key.
