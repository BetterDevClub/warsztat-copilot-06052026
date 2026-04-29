# Prompt

Add an admin endpoint that lets an `Owner` attach a free-form note to a `StaffMember`. Notes are tenant-scoped, must be 1–2000 characters, can be flagged "pinned", and need to fire a `StaffNoteAdded` domain event so we can wire it to the audit log via the outbox later. Cover with one unit test (handler) and one integration test (endpoint).
