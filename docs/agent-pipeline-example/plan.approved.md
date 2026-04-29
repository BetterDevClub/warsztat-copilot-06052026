APPROVE-WITH-EDITS

# Plan: Add staff note endpoint

## 1. Problem
We need an admin-only endpoint to attach free-form notes to a `StaffMember`. Notes are tenant-scoped, audit-relevant, and will later be visible in the admin staff drawer. Currently `StaffMember` has no notes collection and no add-note slice exists.

## 2. Proposed approach
Single VSA slice under `Features/Staff/AddNote/`. New aggregate-child entity `StaffNote : ITenantScoped` in `Domain/Staff/`. Handler creates note, raises `StaffNoteAdded` domain event (picked up by outbox via existing dispatcher). Validation in `*Validator`. Authorization: **`RequireStaff`** (Owner OR Staff — staff members can leave internal notes for context). Tenant resolution stays with the global query filter.

## 3–8: (unchanged from plan.md)

> **Human edits vs `plan.md`:**
> - Sec 2 + Sec 4 (auth): changed from `RequireOwner` to `RequireStaff`. Staff should be able to add internal notes (matches existing `AddBookingInternalNote` slice behavior).
> - Sec 6 (integration tests): rename `Post_AsStaffRole_Returns403` → `Post_AsAnonymous_Returns401` (since Staff is now allowed).
