# Connected Tickets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task if native execution is selected. Steps use checkbox syntax for tracking.

**Goal:** Deliver the approved Focused workspace ticket system with accountable task assignment, linked resources, comments, and transactional inventory outcomes.

**Architecture:** Extend equipment_tickets into the canonical company ticket store. Separate task assignees from intended asset recipients; centralize ticket/request/inventory transitions in one transaction service. Keep the existing Requests contract through an adapter and introduce a dedicated Tickets frontend.

**Tech Stack:** Existing ASP.NET/.NET 10, Npgsql/PostgreSQL, React/TypeScript, Lucide, xUnit, Playwright. No new runtime dependencies.

**Spec:** docs/superpowers/specs/2026-10-10-tickets-design.md

## Global Constraints
- Display statuses exactly Pending, Approved, Assigned, In Progress, Complete, Canceled.
- Task assignees are company login users; intended equipment recipients are directory employees.
- All means every currently enabled company member, including members added later.
- Preserve existing equipment request and ticket IDs; do not fabricate legacy attribution.
- Reference-only asset links never mutate inventory. Inventory outcomes and ticket status commit atomically.
- Match docs/ui-design-rules.md and the approved Focused workspace at the actual website scale.
- No production deployment. Back up local database before migration. This checkout has no Git metadata; preserve changes without inventing commits or a worktree.

## Review Focus
- A referenced asset is reassigned elsewhere after ticket preparation: completion/cancellation rejects conflicting ownership without overwriting it (Task 3).
- A task assignee loses company access mid-session: mutations revalidate membership and comments remain historical (Task 2).
- Linked document/asset becomes inaccessible or is deleted: details show unavailable link without leaking content (Tasks 2 and 6).
- A legacy request already owns Assigned assets: ticket completion validates without duplicate assignment/audit entries (Tasks 3 and 4).
- Long titles, many assignees, and multiline comments: desktop/mobile details wrap and content scrolls below fixed headers (Task 5).

## Files and boundaries
- Create database/migrations/012-connected-tickets.sql: canonical metadata and related tables.
- Create backend/Clary.Api/Tickets/TicketContracts.cs, TicketAccess.cs, TicketRepository.cs, TicketEndpoints.cs: DTOs, authority, persistence, API.
- Create backend/Clary.Api/Tickets/TicketTransitions.cs and TicketInventory.cs: transactional workflow and inventory outcomes.
- Modify backend/Clary.Api/Inventory/EquipmentRepository.cs, EquipmentContracts.cs, EquipmentEndpoints.cs and Program.cs: request compatibility, dependency registration/routes.
- Create frontend/src/tickets/ticketApi.ts, Tickets.tsx, TicketList.tsx, TicketDetails.tsx, TicketEditor.tsx, TicketComments.tsx, TicketConnections.tsx, tickets.css.
- Modify frontend/src/App.tsx, EquipmentRequests.tsx, ComputerAssetDetails.tsx, AssetHistory.tsx, Documentation.tsx, InventoryReports.tsx: stable connected navigation and reporting.
- Create backend/Clary.Tests/TicketTests.cs, TicketInventoryTests.cs, TicketCompatibilityTests.cs and frontend/tests/tickets.spec.ts, ticket-links.spec.ts, ticket-reports.spec.ts.

### Task 1: Canonical schema and contracts
**Interfaces:** TicketDto includes ID, requestId, title, description, type, status, revision, dueDate, recipientEmployeeId, departmentId, locationId, creator/approver identity and dates, assignmentMode (Users/All), assignee user IDs, asset/document connections, and allowedActions. TicketInput supplies those editable fields and expected revision for updates. TicketAssetInput includes assetId and outcome Reference/Assign/Repair/Retire plus optional completionRecipientId/locationId. TicketPage<T> has items and nextCursor.
- [ ] Add schema/contract tests in TicketTests proving standalone tickets and linked requests can coexist, tenant foreign keys reject cross-company relations, one linked request has one ticket, and all six statuses are accepted.
- [ ] Run dotnet test backend/Clary.Tests --filter TicketTests; confirm failure before migration exists.
- [ ] Implement migration 012: make request_id nullable; canonical ticket statuses; metadata; ticket_assignees, ticket_asset_links (outcome plus baseline revision/ownership), ticket_asset_reservations (unique company/asset), ticket_document_links, ticket_comments, ticket_activity. Add deterministic keyset paging indexes and scoped foreign keys. Store actor ID and display-name snapshots in activity/comments.
- [ ] Migrate Ready/Completed/Cancelled ticket states to Assigned/Complete/Canceled, leave request states intact, populate existing request recipient/type/date/link data, and backfill known creator/approver from audit only when reliable. Do not mutate legacy inventory.
- [ ] Define DTOs in TicketContracts.cs and rerun tests to PASS.

### Task 2: Access, metadata, comments, activity, and API
**Interfaces:** TicketRepository.ListAsync(CompanyContext, TicketQuery, CancellationToken) returns TicketPage<TicketDto>; GetAsync(context,id,ct) returns nullable TicketDto; SaveAsync(context,id?,TicketInput,ct) returns TicketDto; AddCommentAsync(context,id,TicketCommentInput,ct) returns TicketCommentDto. TicketAccess revalidates live company membership inside mutation transactions without granting general Write to assigned users.
- [ ] Add TicketTests assertions: admin sees all, ordinary user sees own/All; Editor management requires existing Write/Approve authority; removed membership cannot comment; outsider/foreign tenant cannot read/write; comments only assignees/administrators; All includes later active members.
- [ ] Run filtered tests and confirm failures.
- [ ] Implement scoped repository queries, new /api/tickets GET/POST, /{id} GET/PUT, /{id}/comments GET/POST, /{id}/activity GET, /assignees GET, /settings GET/PUT. Settings persists optional onboarding guide document ID and requires company Settings capability. Assignee choices expose only active user ID/display name, not account-admin data.
- [ ] Cap title 200, description 8000, comment 4000; due date required for new records; use bound parameters and safe bounded page sizes. Validate connected IDs and capabilities; resource errors do not disclose foreign records. Preserve unavailable historical links without returning unauthorized resource details. Limit comments/activity pages to 30, ticket/assignee lists to 50 by default, maximum 100.
- [ ] Reuse authenticated endpoint middleware and session/antiforgery protections; register repository/endpoints in Program.cs. Test invalid input, disabled account, inaccessible/deleted links and paging. Rerun to PASS.

### Task 3: Inventory transitions
**Interfaces:** TicketTransitions.ActionAsync(context,id,TicketActionInput,ct) returns TicketDto; action input includes action, expectedRevision and optional fulfillment selections. TicketInventory executes under the same connection/transaction and ticket lock. Actions approve, assign, start, complete, cancel, send-for-repair; allowedActions generated server-side.
- [ ] Add TicketInventoryTests for insufficient stock => unchanged ticket/assets; exact matching quantity; simultaneous claims => at most one owner; completion => Assigned to intended employee; cancellation => Available and recipient cleared for this ticket's assignment; unrelated/reassigned assets unchanged; reference completion => no inventory writes; repair complete => Assigned/Available; retirement => Retired; duplicate/stale transition => conflict without duplicate history.
- [ ] Run tests and confirm failures.
- [ ] Implement row/revision locks and unique reservations, type/category matching, active recipient/location validation, baseline ownership checks, sorted asset lock order. Pending may have assignees; approve chooses Approved or Assigned; start/complete allowed to assignees/admin after approval; managers assign/cancel; all transitions revalidate current access.
- [ ] Reserve fulfillment assets before handover; Available inventory/kit availability/automatic allocation must exclude active reservations. Record reservation activity without incorrectly setting Assigned. Completion applies explicit outcomes and commits all asset revisions, directory fields, inventory_activity, ticket state, linked request state and audit together.
- [ ] Repair start records prior owner/condition and sets In repair; repair cancellation does not force Available. Release reservations on cancellation; only release actual assignments with proven ticket ownership. Show completion effects before action. Run TicketInventoryTests and InventoryAuditTests to PASS.

### Task 4: Existing equipment requests compatibility
**Interfaces:** Existing EquipmentAction and RequestInput remain compatible. Requests use TicketTransitions via explicit request-to-ticket state mapping Pending/Approved/Ready/Completed/Cancelled; Ready maps to Assigned ticket without conflating technician IDs and employees.
- [ ] Add TicketCompatibilityTests: existing assigned request completion is idempotent for asset ownership, request/ticket share status changes both directions, canceled assignment returns Available, failed action rolls back both records, create produces exactly one ticket.
- [ ] Run tests and confirm failures.
- [ ] Extract shared allocation/assignment helpers from EquipmentRepository into TicketInventory as needed; retain ImmediateAssignment for old requests and track its asset ownership so cancellation is safe. New request-backed ticket reservations fulfill the kit snapshot; standalone Equipment/Onboarding editor chooses an active kit and captures its lines.
- [ ] Keep /api/equipment/tickets/{id} compatibility adapter; ensure request action routes invoke canonical workflow. Update existing equipment/onboarding tests for intended outcomes, then run EquipmentTests, TicketCompatibilityTests, TicketInventoryTests to PASS.

### Task 5: Focused Tickets workspace at website scale
**Interfaces:** Tickets receives initialTicketId, prefill {assetId?,documentId?}, onOpenDocument(id), onOpenRequest(id); ticketApi wraps authenticatedFetch with typed contracts from Task 1 and abort signals. TicketDetails uses server allowedActions, never client assumptions for authority.
- [ ] Add tickets.spec.ts covering list/select, create one/multiple/All task assignment, approval metadata/dates, comments below information, status actions, recipient distinction, error/conflict refresh, scroll/keyboard/modal close, and long-title/many-assignee/mobile wrapping.
- [ ] Run npx.cmd playwright test tests/tickets.spec.ts --reporter=line in frontend; confirm missing workspace behavior fails.
- [ ] Implement compact list left and larger detail right, reuse website controls/focus/modal conventions. Page heading 22px, subtitle 12px, panel gap 10px, desktop page padding 28px top/30px horizontal. Header Search/filter/New ticket grouped right. No large statistic cards; compact six-status summary. Details header fixed; content scrolls with metadata, information, linked resources, comments then activity. Footer actions and inventory-effects preview remain visible.
- [ ] Editor supports due date, type, intended recipient/location, multi-user/All assignment, fulfillment kit and asset/document connections. Render read-only/disabled actions from allowedActions. Replace Tickets branch in App.tsx; preserve module navigation on reload and clear requests on company/user changes.
- [ ] Run frontend build and tickets browser suite; inspect desktop/narrow screenshots, fix overflow and scale until PASS.

### Task 6: Inventory/documentation/request connections
**Interfaces:** App owns navigation intents to ticket/document/request; prefills use stable IDs. TicketConnections resolves allowed link metadata, ComputerAssetDetails keeps information/history, AssetHistory opens ticket links via onOpenTicket(id).
- [ ] Add ticket-links.spec.ts proving create-from-asset/doc prefill, request opens canonical ticket, asset history opens it, forbidden/missing resource links show unavailable, and onboarding configured guide auto-links without title guessing.
- [ ] Run browser test and confirm failures.
- [ ] Add Create ticket actions in asset details and document toolbar; preserve existing Close/Edit behavior. Add canonical ticket navigation from Requests and actor-attributed asset history. Ticket detail opens asset dialog and documentation page. Add guide selection to ticket settings with existing permissions; when unset present optional guide chooser rather than fabricated content.
- [ ] Ensure linked-resource previews use their own permissions and no unauthorized inventory contents are exposed to task-only users. Run ticket-links suite and navigation-restore regression to PASS.

### Task 7: Reports, local migration, and verification
**Interfaces:** /api/tickets/reports accepts filter/subject query, returns paginated ticket/workload/repair rows and summary, uses same visibility predicate. Subjects Tickets, User workload, Asset repairs; frontend reporting preserves selected columns and safe CSV handling.
- [ ] Add backend report assertions and ticket-reports.spec.ts for overdue excluding terminal states, distinct ticket totals, multi-user per-person workloads, shared All bucket, repair per asset and CSV-selected columns.
- [ ] Run tests and confirm failures, then implement report query and InventoryReports subjects/filters. Include dates/approval/status/person/assignee/asset links and pagination; use report-level server aggregation rather than counting only a page.
- [ ] Run focused backend ticket/equipment/audit/isolation suites and frontend ticket/report/navigation suites; run npm.cmd run build --prefix frontend. Re-run only affected checks after fixes.
- [ ] Back up current local PostgreSQL database into C:/Users/sapro/Clary-Lab-Backups/tickets-20261010 before applying migration 012 via existing startup migration tooling. Confirm migration success, restart API hidden, verify session endpoint and ticket page.
- [ ] Add a bounded set of explicitly Demo-labelled tickets/comments/activity to UNIO-Test only (company 0972c761-87a0-43c4-93d1-be98ecec612a), preserve existing records; use genuine actor attribution for real actions. Capture desktop/mobile final screenshots and inspect readability/alignment.
- [ ] Review all spec requirements, resolve failures and report actual results/limits. No commits available in this checkout. Deliver current enhancement and wait for user review.

## Execution handoff
Plan reviewed for coverage, interface consistency, conflicting ownership, access revocation, legacy compatibility, missing resources, and responsive layout. Recommended execution: native in this session, because the canonical transitions and request adapter share tightly coupled transaction rules. User review of this plan and execution method precedes product implementation.
