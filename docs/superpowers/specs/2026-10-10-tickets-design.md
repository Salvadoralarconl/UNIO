# Connected tickets

## Agreed outcome
Tickets becomes a company task workspace connected to equipment requests, inventory, people, locations, documentation, and reports. Preserve the approved neutral styling, compact spacing, vertically centered cells, fixed panel headers, scrollable content, and modal Close buttons/outside dismissal.

## Ticket records and ownership
Each ticket has a title, description, type (Support, Equipment, Onboarding, Repair, Retirement), status, due date, optional intended recipient employee, department/location, linked assets, linked documentation pages, and optional equipment request. Task assignees are company login users; the intended equipment recipient is a directory employee. These are separate fields and must never be inferred from one another.

Assign to one user, multiple users, or All. Proposed default: All means every currently enabled company member, including members added later. Named assignments remain explicit. Disabled users retain historical attribution but cannot act.

Record creator and creation time, approver and approval time, and dated activity identifying the acting profile. Preserve display-name snapshots for historical events together with actor IDs; do not invent creators or approvers when migrating legacy records. Comments show author and date under ticket information.

## Status and permissions
Display exactly Pending, Approved, Assigned, In Progress, Complete, Canceled. Keep approval metadata separate from task assignment: a pending ticket may already have assignees, but approval is still required. Approval moves Pending to Assigned when task assignees exist, otherwise Approved. Adding assignees to an Approved ticket moves it to Assigned. An assigned user or administrator can start and complete approved work. Only an authorized approver/administrator approves, changes assignment, cancels, or changes the inventory plan. Assigned users and administrators can comment. Administrators see company tickets; other users see tickets assigned to them or All, subject to current company membership. Linked resource access is checked separately; ticket access must not grant unrestricted inventory/documentation access.

Terminal tickets retain links, comments, and history. No reopening in the first version. Due date is required for new tickets; legacy missing dates show a dash. Overdue means due date before today's company/user-local date and status neither Complete nor Canceled.

## Inventory outcomes
Each linked asset has an explicit role: Reference only, Assign to recipient, Repair, or Retire. Reference links never change inventory automatically. Show the proposed completion result in the ticket before completion.

Equipment/onboarding: choose matching Available stock automatically or explicitly, reserve it for the ticket without falsely claiming delivery. Completion atomically sets all fulfillment assets to Assigned with the intended employee, department, and optional destination location. Insufficient stock, wrong type/quantity, inactive recipient, stale revision, or ownership conflict blocks the entire completion. Repeated completion cannot duplicate assignments or history.

Repair: sending to repair records previous recipient/location/status and sets In repair. Completion returns Assigned to the intended recipient or Available if there is no recipient. Retirement completion sets Retired and clears current recipient. Cancellation of equipment/onboarding releases reservations and returns assets assigned by that ticket to Available with recipient cleared. Do not clear ownership created by a later ticket or unrelated action: detect that conflict and require resolution. Cancellation of repair preserves ownership and condition; no automatic Available reset for reference, repair, or retirement links.

Every inventory mutation and ticket transition writes actor-attributed history in the same database transaction. Asset history includes an accessible ticket link. Lock stock and reservations and use revision checks so simultaneous tickets cannot claim the same asset.

## Requests and compatibility
Keep existing equipment request IDs and linked ticket IDs. Extend the existing ticket table into the canonical ticket store rather than create a second competing workflow. Make request_id optional for standalone tickets; retain one ticket per linked request. Translate existing Ready to Assigned for ticket display, Completed to Complete, and Cancelled to Canceled. Requests retain their existing contract states through an explicit adapter. Existing assets already assigned through requests remain assigned; completion validates their ownership instead of assigning them twice. Both Requests and Tickets call the same transition/inventory service so status, cancellation, and fulfillment stay consistent. Existing ImmediateAssignment behavior remains supported through this adapter until the request UI is migrated to reservation-based handover.

## Connected navigation
Ticket details show intended person/location separately from task assignees, linked assets with names/tags and information dialogs, the linked request, and Helpful documentation above comments. Documentation links use stable IDs and respect deletion/access changes. Create ticket actions from an asset or documentation page prefill that link. Opening a request's ticket navigates to the same ticket workspace/detail.

Onboarding documentation uses a company-configured guide ID, not a title guess. If no guide exists, allow selecting one; do not create fabricated documentation. Additional pages can be linked manually.

## Approved visual direction
Use the Focused workspace variant approved October 10: compact ticket list on the left, dominant selected-ticket detail on the right. Match actual website scale from docs/ui-design-rules.md: 22px page heading, 12px subtitle, 10px panel gaps, existing controls and table typography. Preview content is illustrative, not seed data. Keep comments directly underneath information and connections; activity follows comments. At narrow widths, stack the list and detail.

## Workspace and reports
Replace the current Tickets rendering of EquipmentRequests with a dedicated ticket list/detail view, using the same approved card and control patterns. Search, filter icon, New ticket stay grouped at the right. Filter by status, type, assignee, recipient, asset, location, and overdue. Details show status/approval/dates, information/connections, comments, and an activity section. Forms use existing custom dropdown/focus styles.

Extend Reports with Tickets, overdue tickets, workload per user, and repairs per asset. Multi-user tickets count once per assigned user's workload; company totals count distinct tickets. All-assigned tickets appear as a separate shared-work bucket to avoid multiplying workload totals. CSV exports preserve current column selection and formula escaping.

## Storage/API and verification
Add a forward migration for ticket metadata, user assignments, asset outcomes/reservations, document links, comments, and activity. Tenant-scoped composite foreign keys and permission checks apply to every relation. Keep list endpoints paginated and detail history/comments independently paginated. Validate comments/descriptions, reject foreign-company IDs, and protect mutations with the existing session/antiforgery mechanisms. Back up the local database before applying the migration; no production deployment is included.

Backend integration checks cover approval attribution, multiple/All assignments, comment permissions, tenant isolation, atomic fulfillment, competing reservations, cancellation ownership conflicts, repair/retirement outcomes, and legacy request synchronization. Browser checks cover list/detail navigation, creation from assets/docs, comments, filters, completion/cancellation inventory effects, report exports, responsive scrolling, and approved modal styling. Demo examples are explicitly labeled and restricted to the existing test company.

## Implementation sequence
1. Canonical ticket storage, permissions, transitions, and inventory transaction service.
2. Dedicated Tickets workspace with assignees, dates, links, comments, and history.
3. Asset/document/request entry points, onboarding guide configuration, and reports.
4. Focused integration/browser verification and local demo examples.

User approved the feature design and Focused workspace visual direction on October 10, 2026. Implementation plan follows this specification.

## Ticket templates (additional user request)
Configuration provides reusable named templates with type, inventory kit, helpful documentation, named users or All, instructions, and a checklist. New tickets copy template values while recipient, department, location, and due date remain specific to the request. Template changes never rewrite existing tickets. Assignees and administrators can check off steps; each check records the acting profile and time, using ticket revision checks. Preserve the compact application styling. Required equipment is reserved before completion and displayed with brand, model, and asset tag. Status actions live in a header icon menu; both list and detail headers have the same height.
