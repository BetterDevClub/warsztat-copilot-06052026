# Plan: Add staff note endpoint

## 1. Problem
We need an admin-only endpoint to attach free-form notes to a `StaffMember`. Notes are tenant-scoped, audit-relevant, and will later be visible in the admin staff drawer. Currently `StaffMember` has no notes collection and no add-note slice exists.

## 2. Proposed approach
Single VSA slice under `Features/Staff/AddNote/`. New aggregate-child entity `StaffNote : ITenantScoped` in `Domain/Staff/`. Handler creates note, raises `StaffNoteAdded` domain event (picked up by outbox via existing dispatcher). Validation in `*Validator`. Authorization: `RequireOwner`. Tenant resolution stays with the global query filter.

## 3. Files to create / modify
| Path | Action | Rationale |
|------|-------|--------------|
| `src/BookSlot.Features/Features/Staff/AddNote/AddStaffNote.cs` | create | Endpoints + Command + Response |
| `src/BookSlot.Features/Features/Staff/AddNote/AddStaffNoteHandler.cs` | create | Sealed handler |
| `src/BookSlot.Features/Features/Staff/AddNote/AddStaffNoteValidator.cs` | create | FluentValidation rules |
| `src/BookSlot.Domain/Staff/StaffNote.cs` | create | New entity (ITenantScoped, audit fields, `StaffNote.Create` factory returning `Result<StaffNote>`) |
| `src/BookSlot.Domain/Staff/Events/StaffNoteAdded.cs` | create | Domain event |
| `src/BookSlot.Domain/Staff/StaffMember.cs` | modify | Add `Notes` navigation collection + `AddNote(...)` method that raises the event |
| `src/BookSlot.Infrastructure/Persistence/Configurations/StaffNoteConfiguration.cs` | create | EF mapping (PK, FK to Staff, indexes, length 2000) |
| `src/BookSlot.Infrastructure/Persistence/AppDbContext.cs` | modify | `DbSet<StaffNote>` + apply config |
| `src/BookSlot.Infrastructure/Persistence/Migrations/<ts>_AddStaffNotes.cs` | create | `dotnet ef migrations add AddStaffNotes -p src/BookSlot.Infrastructure -s src/BookSlot.MigrationRunner -o Persistence/Migrations` |
| `tests/BookSlot.UnitTests/Staff/AddStaffNoteHandlerTests.cs` | create | 3 cases: happy, tenant unresolved → `Result.Failure`, staff not found → `Result.NotFound` |
| `tests/BookSlot.IntegrationTests/Staff/AddStaffNoteEndpointTests.cs` | create | 201 happy, 400 invalid (empty), 403 non-Owner, 404 unknown staff |
| `docs/ARCHITECTURE.md` | modify | Add `Staff/AddNote` to the slice catalog table |

## 4. API contract
- **Method + path:** `POST /api/staff/{staffId:guid}/notes`
- **Auth:** `RequireOwner`
- **Request:**
  ```json
  { "content": "string (1..2000)", "isPinned": false }
  ```
- **Response:** `201 Created` with `{ "noteId": "<guid>" }`, `Location: /api/staff/{staffId}/notes/{noteId}`
- **Errors:** `400` (validation), `403` (role), `404` (staff not found in tenant), `401` (auth)
- **Tenant scope:** yes — both `StaffMember` and `StaffNote` are `ITenantScoped`

## 5. Domain & data model
- `StaffNote(Id, TenantId, StaffMemberId, Content, IsPinned, CreatedAt, CreatedByUserId)`
- `StaffNote.MaxContentLength = 2000`
- `StaffMember.AddNote(...)` is the only public way to create a note → it appends to `Notes` and raises `StaffNoteAdded`.
- Migration: `AddStaffNotes` (creates `StaffNotes` table with FK + indexes on `StaffMemberId, IsPinned`).
- Domain event flows through outbox (existing `OutboxDispatcherJob`); no new wiring.

## 6. Tests
**Unit (`AddStaffNoteHandlerTests`)**:
- `HandleAsync_ValidCommand_PersistsNoteAndReturnsResponse`
- `HandleAsync_TenantUnresolved_ReturnsUnauthorizedResult`
- `HandleAsync_StaffNotFound_ReturnsNotFoundResult`

**Integration (`AddStaffNoteEndpointTests`)**:
- `Post_ValidPayload_AsOwner_Returns201WithNoteId`
- `Post_EmptyContent_Returns400WithValidationErrors`
- `Post_AsStaffRole_Returns403`
- `Post_UnknownStaffId_Returns404`

## 7. Risks and decisions requiring approval
- **Migration name** is `AddStaffNotes`. Reversal works (drops table). No data backfill.
- We're adding `Notes` navigation to `StaffMember` (loaded only on demand — no eager include). Acceptable for current call sites.
- **Authorization choice:** `RequireOwner` (not `RequireStaff`). Need confirmation that staff themselves shouldn't add notes about teammates.

## 8. Out of scope (intentionally excluded)
- List/get/delete note endpoints (separate slices).
- Rendering notes in Blazor admin UI.
- Pinning logic beyond a flag.
- OpenAPI doc beyond what `.WithName/.Produces` give us.
