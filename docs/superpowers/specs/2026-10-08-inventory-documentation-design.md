# Inventory and linked computer documentation

Date: 2026-10-08
Status: proposed design for review

## Outcome

Register computers, assign them to employees and departments, and automatically show their current records in Documentation under Inventory / Computers. Keep the established interface style. Exact table columns, in order:

Computer · IP address · Assignee · Department · Location · Software status · Last seen

Inventory owns the records. The documentation contains a live linked table rather than a second independently editable copy. Adding or editing an asset updates the table without manual copying. Prose written around the table remains editable and is preserved.

## First implementation

Enable Add asset for computers. Collect a required unique asset tag and computer name, optional serial number, IP address (IPv4 or IPv6), assignee, department and location. Store assignment state separately from software health: Available, Assigned, In repair, Retired. Empty optional values display an em dash. Return field-specific validation errors and preserve form input on failed saves.

Inventory landing loads real persisted records, category counts and recently updated equipment. Computer records can be opened and edited. Search covers computer name, asset tag, serial, IP address, assignee, department and location. No invented inventory rows. Other equipment categories remain visible and empty until their registration flows are added.

Use a database-backed asset service and REST endpoints for list, create, details and update. Updates use a revision token to reject stale edits. Asset edits and their activity records commit atomically. Preserve the current local-only deployment model; this design does not grant remote deployment or public exposure.

## Documentation connection

Create or reuse the root Inventory folder and its Computers child folder without changing existing unrelated folders. Place a managed Computer inventory page there. If a document named Computers already exists in that folder, let the user select it before attaching a block; do not overwrite it by title alone.

Add a linked-inventory block type to the rich document schema, reader and editor. The block stores an inventory filter, not copied asset rows. Its cells are read-only in Documentation; edit records in Inventory. The editor preserves this block when editing nearby text and allows normal content before and after it. Newly created managed pages start as drafts.

Opening the linked page fetches the current computer rows. If already open, refresh after same-app asset mutations and on window focus; show loading and refresh errors clearly. A failed load must not look like an empty inventory. No background polling requirement in the first implementation.

Live asset changes do not automatically approve documents or silently reindex mutable asset information into Brie. Existing approved prose stays governed by the current approval process; live inventory is displayed as current operational data. AI inventory queries would need their own later live-data integration.

## Software status and monitoring

Before a computer has reported software information: Not checked, and Last seen is an em dash. Saving or editing an asset does not fabricate a check-in timestamp. Planned monitored statuses are Up to date, Missing software and Requires attention. The source of each status is an actual check-in compared with a required-software policy, not a manually inferred healthy state.

Use a stable asset identity for future monitoring; IP addresses and employee names are not identifiers. Keep computer check-ins, installed software observations and required-software policies separate from manually maintained asset metadata. Preserve last-known observations and show stale/offline state alongside them rather than marking an offline machine healthy.

Windows-first monitoring is an assumption to confirm before building the device component. The device agent and controlled software deployment are a subsequent implementation stage: authenticated device enrollment, installed-software inventory, heartbeat, policy evaluation, approved update jobs, installation results and history. Installing agents or deploying updates to real computers requires explicit targeting and authorization. Do not add placeholder controls that claim to install updates or check software successfully.

## Visual behavior

Reuse the current off-white shell, thin neutral borders, black icons, compact rounded controls and existing typography. Keep the compact category cards and bottom activity summary. Table status badges include readable text, not color alone. Tables scroll within their panels on small screens. Forms and navigation support keyboard use.

## Verification

Database integration checks: persisted assets and assignments, unique tags, invalid addresses, stale edit rejection, activity atomicity. Documentation checks: repeat provisioning does not duplicate folders/pages, existing content survives linking and editing, live rows change after asset edits, draft/approval rules are preserved, failed loads are distinct from empty inventory. Browser checks: register and edit a computer, reopen after reload, search/filter, exact column labels, assignment and department synchronization, Not checked and absent Last seen before monitoring, empty state and mobile layout. Preserve existing documentation/editor and chat navigation regressions.

## Delivery order

1. Asset persistence, validation and activity.
2. Computer registration, details/editing and real inventory landing data.
3. Linked documentation block and safe page provisioning.
4. End-to-end verification and local deployment.
5. Separately design and implement the Windows device agent, software policies and approved update execution.

No changes to product code are included in this design document. The repository has no Git metadata; no design commit can be made here.
