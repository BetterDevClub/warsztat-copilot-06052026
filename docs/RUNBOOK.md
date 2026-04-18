# Runbook

Operational notes for running BookSlot locally and in production.

## Local development

### First run

```powershell
docker compose up -d postgres redis mailhog seq
dotnet build BookSlot.slnx
dotnet run --project src/BookSlot.Web   # applies migrations + seeds dev data
dotnet run --project src/BookSlot.Api   # in another terminal
dotnet run --project src/BookSlot.Worker
```

The Web host owns migrations + seeding (DevDataSeeder). Don't try to start the API against a fresh DB without first booting Web at least once.

### Reset everything

```powershell
docker compose down -v
docker compose up -d postgres redis mailhog seq
dotnet run --project src/BookSlot.Web   # re-applies migrations
```

### Default credentials

| Role  | Email                | Password        |
| ----- | -------------------- | --------------- |
| Admin | `admin@bookslot.dev` | `BookSlotDev1!` |

Seq admin password (when prompted): `BookSlotDev1!`.

## Health checks

| Endpoint         | Meaning                                                                |
| ---------------- | ---------------------------------------------------------------------- |
| `/health/live`   | Process responsive. Used by Docker `HEALTHCHECK` + load balancers.     |
| `/health/ready`  | Postgres reachable + Redis reachable + outbox lag healthy + leader OK. |
| `/health`        | Full report (all checks, JSON via UI client).                          |

If `/health/ready` fails, check:

1. `docker compose ps` — is Postgres / Redis up?
2. Worker logs — is `LeaderElectionHostedService` reporting "leader acquired"?
3. Seq — search for `"Outbox lag"` warnings; if dispatcher fell behind, restart the Worker.

## Common operations

### Apply pending migrations

```powershell
dotnet ef database update --project src/BookSlot.Infrastructure --startup-project src/BookSlot.Web
```

### Generate a new migration

```powershell
dotnet ef migrations add <Name> --project src/BookSlot.Infrastructure --startup-project src/BookSlot.Web
```

### Rotate JWT signing key (Production)

1. Generate a new 256-bit key: `[Convert]::ToBase64String((1..32 | % { Get-Random -Min 0 -Max 256 }))`.
2. Update `JwtOptions:SigningKey` in your secrets store.
3. `ProductionSecretsValidator` will fail-fast at boot if the new value still contains `dev-`/`change-me`/`placeholder` or is shorter than 32 characters.
4. Roll the API pods. Existing access tokens become invalid; clients fall back to refresh-token flow.

### Investigate a failed booking

1. Grab the `X-Correlation-Id` from the response (or browser network tab).
2. In Seq, filter `CorrelationId = '<id>'` to see the full request chain across Web → Api → Worker.
3. The traces in Seq link to the same correlation id via OTLP.

### Webhook retries

`WebhookDeliveryJob` retries with exponential backoff up to 6 attempts. To force a redelivery:

```sql
UPDATE webhook_deliveries
   SET status = 'Pending', attempt_count = 0, next_attempt_at = now()
 WHERE id = '<delivery-id>';
```

## Deployment

- CI: `.github/workflows/ci.yml` runs build + unit + architecture tests on every push/PR; integration tests (Testcontainers) run on push to `master`/`main`.
- Images: `.github/workflows/docker.yml` builds `bookslot-api`, `bookslot-web`, `bookslot-worker` and pushes them to GHCR on push to default branch and on `v*.*.*` tags.
- Promote-to-prod: gate the deploy job on a manual `environment: production` approval (configure the GitHub environment secret + reviewers in repo settings).

## Troubleshooting

| Symptom                                                  | Likely cause                                                                | Fix                                                                                          |
| -------------------------------------------------------- | --------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- |
| `dotnet build` fails with file lock on `BookSlot.*.dll` | Web/Api/Worker still running                                                 | `Get-Process BookSlot* \| Stop-Process -Force`                                              |
| 401 on every API call from the SPA                       | JWT expired and refresh failed                                              | Open dev tools → check `/auth/refresh` response. Most often the refresh cookie was cleared.  |
| Public booking returns 429                               | `bookings-public` rate limit (10/min/IP) hit                                | Expected under load — extend window in `Api/Program.cs` if a real customer needs more.       |
| Login returns 429                                        | `auth-sensitive` rate limit (5/min/IP)                                      | Expected under brute-force probing. Investigate; do not raise blindly.                       |
| `aspire-dashboard` image fails to pull                   | Network restriction on `mcr.microsoft.com`                                  | Skip it — Seq receives both logs and OTLP traces. Drop the service from your `docker compose up`. |
| Architecture tests fail after refactor                   | A slice took a dependency on another slice                                  | Move the shared type to `BookSlot.Features/Shared/` or duplicate it inside the slice.        |
