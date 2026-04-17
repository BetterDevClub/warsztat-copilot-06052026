# Projekt startowy warsztatu: BookSlot — Appointment Booking Platform

> Własny Calendly w .NET. Multi-tenant system rezerwacji wizyt.
> Stack: ASP.NET Core 8 · EF Core · Blazor Server · Worker Service · xUnit · SignalR

---

## 🏗️ Struktura projektu (monorepo)

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
Tenant                    ← organizacja/firma (multi-tenant)
├── TenantSettings        ← timezone, booking buffer, max advance days, branding
├── Staff[]               ← osoby przyjmujące wizyty
├── ServiceType[]         ← typy wizyt (np. "Konsultacja 30min")
└── BookingFormSchema     ← custom pola formularza rezerwacji

Staff
├── AvailabilityRule[]    ← tygodniowy harmonogram (pon 9-17 itd.)
├── AvailabilityOverride[]← wyjątki: urlop, wolne, nadgodziny
└── ServiceType[]         ← jakie usługi świadczy (many-to-many)

ServiceType
├── Duration              ← czas trwania (15/30/45/60/90/120 min)
├── BufferBefore/After    ← padding między wizytami
├── MaxConcurrent         ← ile wizyt jednocześnie (np. grupowe zajęcia)
├── LocationType          ← InPerson | Virtual | Phone
└── Color                 ← kolor na kalendarzu

Booking
├── BookingStatus         ← Pending | Confirmed | Cancelled | NoShow | Completed
├── CancellationToken     ← GUID do anulowania/przełożenia bez logowania
├── RescheduledFromId     ← historia przeplanowań
├── CustomFieldValues[]   ← odpowiedzi na custom pola formularza
└── AuditLog[]            ← kto co zmienił i kiedy

SlotReservation           ← tymczasowa blokada slotu podczas rezerwacji (TTL: 10 min)

WebhookEndpoint           ← outbound webhooks per tenant
WebhookDelivery           ← log prób dostarczenia (status, response, retry count)

NotificationLog           ← historia wysłanych emaili/SMSów

RecurringBooking          ← cykliczne wizyty (co tydzień, co 2 tygodnie)
```

---

## 🔧 Backend — ASP.NET Core Web API

### Moduł: Tenant & Auth
- Rejestracja tenanta (onboarding flow)
- JWT auth z refresh tokenami
- Role: `Owner` | `Staff` | `Viewer`
- Tenant isolation przez middleware (subdomain lub header)
- API keys dla integracji zewnętrznych

### Moduł: Services & Staff
- CRUD dla `ServiceType` z walidacją (FluentValidation)
- CRUD dla `Staff` z przypisaniem do serwisów
- Upload avatara staff (Azure Blob / S3 presigned URL)
- Bulk assign: przypisz serwis do wielu staffów naraz

### Moduł: Availability Engine *(najtrudniejszy — idealny do demo)*

```
GET /availability/{tenantSlug}/{serviceTypeId}?date=2026-05-10&staffId=optional

Algorytm:
1. Pobierz AvailabilityRules dla staff (lub wszystkich staffów danego serwisu)
2. Nałóż AvailabilityOverrides (urlopy, wyjątki)
3. Pobierz istniejące Bookings w danym dniu
4. Odejmij aktywne SlotReservations (tymczasowe blokady)
5. Uwzględnij BufferBefore/After każdej wizyty
6. Uwzględnij MaxConcurrent
7. Konwertuj sloty do timezone klienta
8. Zwróć dostępne okna czasowe
```

> **Demo warsztatowe:** To jest miejsce gdzie Copilot bez `copilot-instructions.md` zrobi błąd —
> timezone edge cases, buffer overlap, concurrent booking limit. Idealne demo przed/po.

### Moduł: Booking (public — bez auth)
- `POST /bookings` — stwórz rezerwację (atomic: reserve slot → create booking → release reservation)
- `GET /bookings/cancel/{token}` — anuluj przez token z emaila
- `GET /bookings/reschedule/{token}` — przeplanuj (zwraca nowe dostępne sloty)
- `POST /bookings/reschedule/{token}` — zatwierdź nowy termin
- Optimistic concurrency (EF Core `RowVersion`) — dwie osoby nie zarezerwują tego samego slotu

### Moduł: Booking Admin (wymaga auth)
- Pełne CRUD na rezerwacjach
- Ręczna rezerwacja przez admina (override dostępności)
- Oznaczanie no-show
- Export do CSV / iCal
- Notatki wewnętrzne do rezerwacji
- Historia zmian (audit log)

### Moduł: Webhooks (outbound)
- Rejestracja endpointów per tenant
- Eventy: `booking.created` | `booking.confirmed` | `booking.cancelled` | `booking.rescheduled` | `booking.no_show`
- Payload signing (HMAC-SHA256)
- **Outbox pattern** — zapis do DB przed wysłaniem, Worker odbiera z kolejki
- Retry config per endpoint (max attempts, backoff)

### Moduł: Reporting
- `GET /reports/bookings?from=&to=` — statystyki: total, confirmed, cancelled, no-show rate
- `GET /reports/busiest-slots` — heat mapa popularności godzin
- `GET /reports/staff-utilization` — ile czasu zajęte vs dostępne

### Moduł: Integrations
- `GET /calendar/{tenantSlug}/{staffId}.ics` — publiczny feed iCal dla Google/Apple Calendar
- Zoom: generowanie linka (mock lub real Zoom OAuth)
- Google Calendar sync (opcjonalnie — przez OAuth2)

---

## 🖥️ Frontend — Blazor Server

### Admin Panel (po zalogowaniu)

**Dashboard**
- Dzisiejsze rezerwacje (timeline view, godzina po godzinie)
- Jutrzejsze rezerwacje (preview)
- Statystyki tygodnia: liczba rezerwacji, no-show rate, top serwis
- Live notification toast gdy wpada nowa rezerwacja → **SignalR**

**Kalendarz wizyt** (`/calendar`)
- Widok tygodnia / dnia / miesiąca (własny komponent Blazor)
- Kolor per `ServiceType`
- Kliknięcie → drawer ze szczegółami rezerwacji
- Real-time update przez SignalR gdy ktoś rezerwuje online

**Lista rezerwacji** (`/bookings`)
- Filtry: data, staff, status, serwis
- Inline actions: potwierdź, anuluj, no-show, notatka
- Eksport do CSV

**Zarządzanie serwisami** (`/services`)
- CRUD z preview jak wygląda booking page
- Formularz z custom polami (drag & drop kolejność pól)

**Zarządzanie staffem** (`/staff`)
- CRUD staff
- **Visual availability editor** — tygodniowa siatka (pon–ndz × godziny), kliknij żeby włączyć/wyłączyć slot
- Override manager: dodaj urlop / wyjątkowe godziny na konkretny dzień
- Miniaturka kalendarza z nadchodzącymi wizytami

**Ustawienia** (`/settings`)
- Tenant settings: nazwa, timezone, booking window (min/max advance)
- Branding: logo URL, kolor akcentu
- Webhook management: lista endpointów, log dostarczeń, manual retry
- Integracje: iCal URL (copy to clipboard), Zoom, Google Calendar

---

### Public Booking Flow (bez auth) — `/book/{tenantSlug}`

```
Krok 1: Wybierz serwis
        → lista kart z nazwą, czasem trwania, opisem

Krok 2: Wybierz staff (lub "Pierwszy dostępny")
        → avatary + nazwy + bio

Krok 3: Wybierz datę
        → mini kalendarz, zablokowane daty bez dostępnych slotów

Krok 4: Wybierz godzinę
        → dynamiczne ładowanie slotów z API dla wybranej daty
        → skeleton loader podczas ładowania
        → po wyborze: tymczasowa rezerwacja slotu (10 min countdown timer widoczny!)

Krok 5: Wypełnij formularz
        → imię, email, telefon + custom pola serwisu
        → countdown timer nadal widoczny (presja czasu = feature, nie bug)

Krok 6: Potwierdzenie
        → "Rezerwacja potwierdzona!" z detalami
        → Przycisk "Dodaj do kalendarza" (iCal download)
        → Link do anulowania (przez token)
```

---

## ⚙️ Worker Service — Background Jobs

### 1. `ReminderDispatcher`
Wysyła email remindery. Sprawdza co minutę rezerwacje na `teraz + 24h` i `teraz + 1h`.
Idempotentny — `NotificationLog` zapobiega duplikatom.

### 2. `SlotLockCleaner`
Co 30 sekund czyści `SlotReservation` których TTL wygasł.
Musi być atomowy — nie może zwolnić slotu który właśnie dostał nową rezerwację.
> **Demo:** klasyczny race condition — idealny przykład gdzie Copilot bez instrukcji generuje błąd.

### 3. `NoShowMarker`
Co 15 minut sprawdza wizyty które się skończyły i nie zostały oznaczone.
Po grace period (np. 30 min po zakończeniu) → auto-mark jako `NoShow`.
Tenant może skonfigurować czy chce to zachowanie.

### 4. `WebhookDispatcher`
Czyta z outbox table. Dla każdej nieposłanej dostawy:
- Próbuje wysłać HTTP POST do endpointu tenanta
- Sukces → `WebhookDelivery.Status = Delivered`
- Fail → exponential backoff (1min → 5min → 30min → 2h → 8h)
- Po max attempts → `Failed`, alert do tenanta
- Dead letter handling — osobna tabela `DeadLetterWebhooks`

### 5. `DailyDigestSender`
Codziennie o 18:00 (per-tenant timezone!) wysyła właścicielowi email z:
- Podsumowaniem dzisiejszego dnia (ile wizyt, ile no-show)
- Preview jutrzejszych wizyt (lista z godzinami)
- Procentem zapełnienia kalendarza w tym tygodniu

### 6. `GoogleCalendarSync` *(opcjonalny)*
Dla tenantów z włączoną integracją — synchronizuje nowe i anulowane rezerwacje.
Obsługuje token refresh. Circuit breaker gdy Google API pada.

### 7. `RecurringBookingGenerator`
Dla cyklicznych rezerwacji generuje kolejne instancje z wyprzedzeniem (4 tygodnie do przodu).
Obsługuje wyjątki gdy slot jest zajęty (skip lub znajdź następny wolny).

### 8. `ReportArchiver`
Co miesiąc generuje raport per tenant, zapisuje do storage, wysyła link emailem.

---

## 🔄 CI/CD — GitHub Actions

```
ci-shared.yml    ← Domain + Application + Infrastructure (ZAWSZE PIERWSZY)
ci-api.yml       ← build → unit tests → integration tests (PostgreSQL container) → artifact
ci-web.yml       ← build Blazor → publish → smoke test (curl /health)
ci-worker.yml    ← build → testy → verify IHostedService registration

deploy.yml (manual + on merge to main)
  1. Run migrations (dotnet ef database update)
  2. Deploy API → health check
  3. Deploy Worker → verify process running
  4. Deploy Web → smoke test
  5. Integration smoke: POST /bookings → verify webhook delivered
  [MANUAL APPROVAL GATE staging → production] ← HITL demo!
```

### Gdzie agent bez `copilot-instructions.md` zawali CI/CD:
- Zbuduje API przed Shared → fail
- Nie doda `services: postgres:` do integration tests → testy padają
- Deploy bez migracji → runtime crash
- Worker deploy przed API → Worker nie ma do czego się podłączyć

---

## 🏛️ Wzorce architektoniczne (materiał dla agentów)

| Pattern | Gdzie | Dlaczego trudny dla Copilota bez instrukcji |
|---------|-------|---------------------------------------------|
| CQRS z MediatR | Application layer | Agent miesza Query z Command, pomija pipeline behaviors |
| Result\<T\> | Wszędzie zamiast exceptions | Agent domyślnie rzuca wyjątki w logice domenowej |
| Outbox Pattern | Webhooks | Agent pomija transakcyjność, robi HTTP call w kontrolerze |
| Optimistic Concurrency | Booking | Agent nie zna `RowVersion`, generuje race conditions |
| Timezone-aware scheduling | Availability + Worker | Agent używa `DateTime.Now` zamiast `DateTimeOffset` |
| Tenant isolation middleware | API | Agent zapomina o filtrowaniu per-tenant w każdym query |

---

*Blueprint v1.0 | 17.04.2026*
