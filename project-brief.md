# Workshop Starter Project: BookSlot — Appointment Booking Platform

> Build your own Calendly with .NET. Multi-tenant appointment booking system.
> Stack: ASP.NET Core 8 · EF Core · Blazor Server · Worker Service · xUnit · SignalR

---

## 🏗️ Project Structure (monorepo)

```
BookSlot/
├── src/
│   ├── BookSlot.Domain/          ← entities, value objects, domain events
│   ├── BookSlot.Application/     ← CQRS (MediatR), DTOs, interfaces
│   ├── BookSlot.Infrastructure/  ← EF Core, email, external APIs
│   ├── BookSlot.Api/             ← ASP.NET Core Web API
│   ├── BookSlot.Web/             ← Blazor Server (admin panel)
│   └── BookSlot.Worker/          ← Worker Service (background jobs)
├── tests/
│   ├── BookSlot.UnitTests/
│   ├── BookSlot.IntegrationTests/
│   └── BookSlot.ArchitectureTests/  ← NetArchTest guardrails
└── .github/
    ├── agents/
    ├── copilot-instructions.md
    └── workflows/
```

---

## 🗄️ Domain Model

### Core Entities

```
Tenant                    ← organization/company (multi-tenant)
├── TenantSettings        ← timezone, booking buffer, max advance days, branding
├── Staff[]               ← people who accept appointments
├── ServiceType[]         ← appointment types (e.g., "30min Consultation")
└── BookingFormSchema     ← custom booking form fields

Staff
├── AvailabilityRule[]    ← weekly schedule (Mon 9-5 etc.)
├── AvailabilityOverride[]← exceptions: vacation, time off, overtime
└── ServiceType[]         ← what services they provide (many-to-many)

ServiceType
├── Duration              ← duration (15/30/45/60/90/120 min)
├── BufferBefore/After    ← padding between appointments
├── MaxConcurrent         ← how many appointments simultaneously (e.g., group sessions)
├── LocationType          ← InPerson | Virtual | Phone
└── Color                 ← calendar color

Booking
├── BookingStatus         ← Pending | Confirmed | Cancelled | NoShow | Completed
├── CancellationToken     ← GUID for cancel/reschedule without login
├── RescheduledFromId     ← rescheduling history
├── CustomFieldValues[]   ← answers to custom form fields
└── AuditLog[]            ← who changed what and when

SlotReservation           ← temporary slot lock during booking (TTL: 10 min)

WebhookEndpoint           ← outbound webhooks per tenant
WebhookDelivery           ← delivery attempt log (status, response, retry count)

NotificationLog           ← history of sent emails/SMS

RecurringBooking          ← recurring appointments (weekly, bi-weekly)
```

---

## 🔧 Backend — ASP.NET Core Web API

### Module: Tenant & Auth
- Tenant registration (onboarding flow)
- JWT auth with refresh tokens
- Roles: `Owner` | `Staff` | `Viewer`
- Tenant isolation via middleware (subdomain or header)
- API keys for external integrations

### Module: Services & Staff
- CRUD for `ServiceType` with validation (FluentValidation)
- CRUD for `Staff` with service assignment
- Staff avatar upload (Azure Blob / S3 presigned URL)
- Bulk assign: assign service to multiple staff at once

### Module: Availability Engine *(most challenging — ideal for demo)*

```
GET /availability/{tenantSlug}/{serviceTypeId}?date=2026-05-10&staffId=optional

Algorithm:
1. Fetch AvailabilityRules for staff (or all staff for given service)
2. Apply AvailabilityOverrides (vacation, exceptions)
3. Fetch existing Bookings on given day
4. Subtract active SlotReservations (temporary locks)
5. Account for BufferBefore/After of each appointment
6. Account for MaxConcurrent
7. Convert slots to client timezone
8. Return available time windows
```

> **Workshop demo:** This is where Copilot without `copilot-instructions.md` will make mistakes —
> timezone edge cases, buffer overlap, concurrent booking limit. Perfect before/after demo.

### Module: Booking (public — no auth)
- `POST /bookings` — create booking (atomic: reserve slot → create booking → release reservation)
- `GET /bookings/cancel/{token}` — cancel via token from email
- `GET /bookings/reschedule/{token}` — reschedule (returns new available slots)
- `POST /bookings/reschedule/{token}` — confirm new date/time
- Optimistic concurrency (EF Core `RowVersion`) — two people can't book the same slot

### Module: Booking Admin (requires auth)
- Full CRUD on bookings
- Manual booking by admin (override availability)
- Mark as no-show
- Export to CSV / iCal
- Internal notes on bookings
- Change history (audit log)

### Module: Webhooks (outbound)
- Endpoint registration per tenant
- Events: `booking.created` | `booking.confirmed` | `booking.cancelled` | `booking.rescheduled` | `booking.no_show`
- Payload signing (HMAC-SHA256)
- **Outbox pattern** — write to DB before sending, Worker picks from queue
- Retry config per endpoint (max attempts, backoff)

### Module: Reporting
- `GET /reports/bookings?from=&to=` — stats: total, confirmed, cancelled, no-show rate
- `GET /reports/busiest-slots` — heat map of popular time slots
- `GET /reports/staff-utilization` — busy time vs available time

### Module: Integrations
- `GET /calendar/{tenantSlug}/{staffId}.ics` — public iCal feed for Google/Apple Calendar
- Zoom: link generation (mock or real Zoom OAuth)
- Google Calendar sync (optional — via OAuth2)

---

## 🖥️ Frontend — Blazor Server

### Admin Panel (after login)

**Dashboard**
- Today's bookings (timeline view, hour by hour)
- Tomorrow's bookings (preview)
- Week stats: booking count, no-show rate, top service
- Live notification toast when new booking arrives → **SignalR**

**Appointment Calendar** (`/calendar`)
- Week / day / month view (custom Blazor component)
- Color per `ServiceType`
- Click → drawer with booking details
- Real-time update via SignalR when someone books online

**Bookings List** (`/bookings`)
- Filters: date, staff, status, service
- Inline actions: confirm, cancel, no-show, note
- Export to CSV

**Service Management** (`/services`)
- CRUD with preview of booking page
- Form with custom fields (drag & drop field order)

**Staff Management** (`/staff`)
- CRUD staff
- **Visual availability editor** — weekly grid (Mon–Sun × hours), click to enable/disable slot
- Override manager: add vacation / special hours on specific day
- Mini calendar with upcoming appointments

**Settings** (`/settings`)
- Tenant settings: name, timezone, booking window (min/max advance)
- Branding: logo URL, accent color
- Webhook management: endpoint list, delivery log, manual retry
- Integrations: iCal URL (copy to clipboard), Zoom, Google Calendar

---

### Public Booking Flow (no auth) — `/book/{tenantSlug}`

```
Step 1: Select service
        → card list with name, duration, description

Step 2: Select staff (or "First available")
        → avatars + names + bio

Step 3: Select date
        → mini calendar, blocked dates with no available slots

Step 4: Select time
        → dynamic slot loading from API for selected date
        → skeleton loader during loading
        → after selection: temporary slot reservation (10 min countdown timer visible!)

Step 5: Fill out form
        → name, email, phone + service custom fields
        → countdown timer still visible (time pressure = feature, not bug)

Step 6: Confirmation
        → "Booking confirmed!" with details
        → "Add to calendar" button (iCal download)
        → Cancellation link (via token)
```

---

## ⚙️ Worker Service — Background Jobs

### 1. `ReminderDispatcher`
Sends email reminders. Checks every minute for bookings at `now + 24h` and `now + 1h`.
Idempotent — `NotificationLog` prevents duplicates.

### 2. `SlotLockCleaner`
Every 30 seconds cleans `SlotReservation` where TTL has expired.
Must be atomic — can't release a slot that just got a new booking.
> **Demo:** classic race condition — perfect example where Copilot without instructions generates bugs.

### 3. `NoShowMarker`
Every 15 minutes checks appointments that have ended and weren't marked.
After grace period (e.g., 30 min after end) → auto-mark as `NoShow`.
Tenant can configure whether they want this behavior.

### 4. `WebhookDispatcher`
Reads from outbox table. For each undelivered delivery:
- Attempts HTTP POST to tenant endpoint
- Success → `WebhookDelivery.Status = Delivered`
- Fail → exponential backoff (1min → 5min → 30min → 2h → 8h)
- After max attempts → `Failed`, alert to tenant
- Dead letter handling — separate `DeadLetterWebhooks` table

### 5. `DailyDigestSender`
Every day at 6:00 PM (per-tenant timezone!) sends owner email with:
- Today's summary (how many appointments, how many no-shows)
- Tomorrow's appointment preview (list with times)
- Calendar fill percentage this week

### 6. `GoogleCalendarSync` *(optional)*
For tenants with integration enabled — syncs new and cancelled bookings.
Handles token refresh. Circuit breaker when Google API is down.

### 7. `RecurringBookingGenerator`
For recurring bookings generates next instances in advance (4 weeks ahead).
Handles exceptions when slot is occupied (skip or find next available).

### 8. `ReportArchiver`
Every month generates report per tenant, saves to storage, sends link via email.

---

## 🔄 CI/CD — GitHub Actions

```
ci-shared.yml    ← Domain + Application + Infrastructure (ALWAYS FIRST)
ci-api.yml       ← build → unit tests → integration tests (PostgreSQL container) → artifact
ci-web.yml       ← build Blazor → publish → smoke test (curl /health)
ci-worker.yml    ← build → tests → verify IHostedService registration

deploy.yml (manual + on merge to main)
  1. Run migrations (dotnet ef database update)
  2. Deploy API → health check
  3. Deploy Worker → verify process running
  4. Deploy Web → smoke test
  5. Integration smoke: POST /bookings → verify webhook delivered
  [MANUAL APPROVAL GATE staging → production] ← HITL demo!
```

### Where agent without `copilot-instructions.md` will fail CI/CD:
- Builds API before Shared → fail
- Doesn't add `services: postgres:` to integration tests → tests fail
- Deploy without migrations → runtime crash
- Worker deploy before API → Worker has nothing to connect to

---

## 🏛️ Architecture Patterns (material for agents)

| Pattern | Where | Why it's hard for Copilot without instructions |
|---------|-------|------------------------------------------------|
| CQRS with MediatR | Application layer | Agent mixes Query with Command, skips pipeline behaviors |
| Result\<T\> | Everywhere instead of exceptions | Agent defaults to throwing exceptions in domain logic |
| Outbox Pattern | Webhooks | Agent skips transactionality, makes HTTP call in controller |
| Optimistic Concurrency | Booking | Agent doesn't know `RowVersion`, generates race conditions |
| Timezone-aware scheduling | Availability + Worker | Agent uses `DateTime.Now` instead of `DateTimeOffset` |
| Tenant isolation middleware | API | Agent forgets per-tenant filtering in every query |

---

*Blueprint v1.0 | 17.04.2026*
