# Computer Inventory and Documentation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for native execution or superpowers:subagent-driven-development if the user chooses delegation. Follow the checkboxes task-by-task.

**Goal:** Register and edit computers and automatically display current asset records in a linked documentation table.

**Architecture:** PostgreSQL owns asset and activity records. A rich-document inventory block references current computer data through the asset API, preserving surrounding prose. Device monitoring and remote update deployment are a later stage; the initial implementation reports Not checked without inventing observations.

**Tech Stack:** Existing .NET 10 / Npgsql backend, PostgreSQL, React 19 / TypeScript / Vite frontend, xUnit and Playwright. No new dependencies required.

**Spec:** ../specs/2026-10-08-inventory-documentation-design.md (approved 2026-10-08).

## Global constraints

- Computer · IP address · Assignee · Department · Location · Software status · Last seen
- Inventory owns the records; documentation displays a live linked table.
- Preserve existing documentation text, approval and indexing rules.
- Before monitoring: Not checked and no Last seen timestamp.
- Preserve established off-white panels, black icons, compact controls and neutral borders.
- Only C:/Projects/Apps/Clary-Lab is writable product scope. SourceDesk is a frozen backup.
- No Git repository exists here. Do not initialize one or attempt commits; verify saved changes with tests.
- Do not install device agents, deploy software updates, expose services publicly, or use paid AI services.

## Review focus

- Duplicate asset tags differing by whitespace/case must fail predictably (Task 1).
- Concurrent edits must preserve the newer record and reject stale input (Tasks 1 and 2).
- Documentation with the same title must never be silently overwritten or attached by title alone (Task 3).
- Loading failures must not appear as zero assets or healthy computers (Tasks 2 and 3).
- Saving rich prose around a linked table must retain the block and must not index mutable inventory rows as approved guidance (Task 3).

## File boundaries

- backend/Clary.Api/Inventory/InventoryRepository.cs: asset persistence, validation, optimistic concurrency and atomic activity.
- backend/Clary.Api/Inventory/InventoryEndpoints.cs: HTTP routes and status mapping; Program.cs registers service/routes.
- database/001-initial.sql: idempotent asset/activity and managed documentation link schema.
- frontend/src/inventoryApi.ts: shared typed asset contracts and API access.
- frontend/src/ComputerAssetEditor.tsx: registration/edit dialog.
- frontend/src/ComputerAssetDetails.tsx: readable record and edit action.
- frontend/src/Inventory.tsx and inventory.css: live landing data and category/search behavior.
- frontend/src/LinkedComputerTable.tsx: live documentation table, refresh and errors.
- backend/Clary.Api/Knowledge/InventoryDocumentation.cs: safe/idempotent page provisioning and explicit linking.
- RichTextEditor.tsx and Knowledge/RichDocument.cs: validated linked block round-trip.
- Documentation.tsx: link controls and reader integration.

## Shared contracts

Asset JSON: id(UUID), assetTag, computer, serialNumber, ipAddress, assignee, department, location (strings; optional values nullable), status(Available|Assigned|In repair|Retired), softwareStatus(Not checked initially), lastSeen(nullable UTC), updatedAt(UTC), revision(integer). Category is Computer for this stage.

Create input: assetTag, computer, optional serialNumber/ipAddress/assignee/department/location, status. Update input adds revision. Required names/tags max100; other text max200; parse IPv4/IPv6 using System.Net.IPAddress.TryParse. Normalize asset tags by trim + invariant uppercase for uniqueness; retain display value. Reject invalid enum values and overlong values. Define these types once per language.

Routes: GET /api/inventory/assets; POST /api/inventory/assets; GET /api/inventory/assets/{id}; PUT /api/inventory/assets/{id}; GET /api/inventory/activity?limit=10. Return 400 validation errors, 404 missing records, 409 duplicate tag/stale revision. No database exception details in responses.

Documentation block JSON: {"type":"inventory","category":"Computer"}. No copied rows, arbitrary URLs, SQL or device credentials. Backend sanitizer and frontend read/build/render recognize this exact block type/filter.

## Task 1: Persistent computer records

- [ ] Add backend/Clary.Tests/InventoryIntegrationTests.cs using the existing random-schema fixture pattern. Test create/list/reload, department and assignment persistence, normalized duplicate tags, valid IPv4/IPv6, invalid/overlong input, missing IDs, stale revision rejection, and exactly one activity event per committed mutation.
- [ ] Run dotnet test Clary.slnx --filter InventoryIntegrationTests with CLARY_TEST_CONNECTION safely populated from local configuration; confirm failures are caused by missing implementation.
- [ ] Extend idempotent schema with inventory_assets (UUID PK, validated metadata, unique normalized tag, revision, UTC timestamps) and inventory_activity (UUID PK, asset FK, event kind, timestamp). Never derive lastSeen from updatedAt.
- [ ] Implement InventoryRepository CreateAsync, ListAsync, GetAsync, UpdateAsync and ActivityAsync. Use parameterized SQL and a transaction for mutation + event. Update WHERE id AND revision; distinguish missing versus stale records. Return shared asset DTOs.
- [ ] Implement InventoryEndpoints.MapInventoryEndpoints and register repository/routes in Program.cs. Map validation/concurrency errors to the contract; preserve API CORS and existing behavior.
- [ ] Rerun integration tests and existing backend suite. Review queries, transaction rollback and input limits before proceeding.

## Task 2: Computer registration and real inventory landing

- [ ] Add frontend/tests/inventory-assets.spec.ts. Mock asset API for registration success, validation/409 errors retaining input, edit/reload, search by department/serial/IP, category counts and server failure distinct from empty state. Assert no Last seen and Not checked initially.
- [ ] Run focused Playwright test and observe expected missing UI failures.
- [ ] Create inventoryApi.ts with typed Asset/CreateAsset/UpdateAsset/Activity and functions listAssets/createAsset/getAsset/updateAsset/listActivity.
- [ ] Create ComputerAssetEditor and ComputerAssetDetails. Reuse established modal styling, keyboard/focus patterns, Save/Cancel, field labels and error states. Registration initially supports computers; assignment status is explicit and separate from software status.
- [ ] Enable Add asset sidebar action in App.tsx and route it to Inventory registration. Implement an open-request prop or shared state rather than a second disconnected asset store. Keep unfinished sidebar sections visibly unfinished.
- [ ] Replace hardcoded zero inventory counts with fetched records. Filter by category/search; load recent rows ordered updatedAt descending and real activity. Keep current table geometry stable when filters change. Click record opens details; saved edits refresh lists/counts/activity. Dispatch a window CustomEvent('inventory-changed') after confirmed mutations for linked documentation refresh.
- [ ] Run focused UI tests plus inventory.spec.ts, inventory-tooltips.spec.ts and suite-navigation.spec.ts. Verify narrow viewport overflow and existing draft preservation.

## Task 3: Live linked documentation

- [ ] Add backend/Clary.Tests/InventoryDocumentationTests.cs for repeat provisioning, preservation of unrelated same-name folders/pages, explicit existing-page linking, draft creation, rich block validation and prose round-trip, unchanged approval behavior and no inventory rows entering canonical indexed prose.
- [ ] Add frontend/tests/inventory-documentation.spec.ts for adding/editing a computer then opening linked documentation, exact column order, department/assignee values, edit surrounding prose without losing the block, refresh on inventory-changed/window focus and failure distinctly reported.
- [ ] Run tests and confirm expected failures before implementation.
- [ ] Implement InventoryDocumentation provisioning under a transaction and an idempotency mapping keyed 'Computer'. Reuse exact-path folders Inventory / Computers only; create missing folders. If existing candidate pages exist, return a selection requirement and let the user explicitly choose a UUID or create a new page. Never attach or overwrite a page based solely on its name. Create managed Computer inventory page as draft. Expose GET /api/inventory/documentation and POST /api/inventory/documentation/link with optional selected documentId; validate selected active wiki documents.
- [ ] Extend RichDocument.cs sanitizer and RichNode contracts with the fixed inventory block. Canonical text includes an explanatory block label, never live asset rows. Preserve existing prose and revision/approval behavior.
- [ ] Extend rich editor build/read to retain a contentEditable=false inventory-block placeholder. Protect its metadata through normal save/reopen and undo, and ensure table-format actions do not treat it as an editable spreadsheet. Render the block with LinkedComputerTable through RichContent.
- [ ] Implement LinkedComputerTable using listAssets filtered Computer and the exact seven column labels. Display blank optional fields as an em dash, real statuses as text badges, timestamps localized with UTC source. Refresh on mount, window focus and inventory-changed with abort/cleanup protection. Distinguish pending, empty and failed states, with Retry.
- [ ] Add a Documentation link control to the computer workflow for first-time provisioning/explicit candidate choice. After connection, show Open documentation and navigate through the existing document/folder flow. Preserve current page prose and keep asset cells read-only here.
- [ ] Run linked-documentation tests plus editor, image, table, formula, link, revision and upload regressions. Review schema compatibility with stored existing documents.

## Task 4: Local end-to-end verification

- [ ] Run all backend tests and frontend production build. Run existing non-live browser tests plus opt-in documentation integration tests; AI quality is a separate known issue, not an inventory acceptance criterion.
- [ ] Deploy locally using scripts/Start-Clary.ps1 -Compose -Provider Ollama. Do not restart while live tests run.
- [ ] Register a clearly named disposable audit computer, assign employee + department + location + IPv6, confirm persistence after reload, linked table update and unchanged prose, stale-update rejection, Not checked / absent Last seen, actual activity, and no false AI-index approval. Remove only exact test fixtures via supported cleanup/test schema tools; preserve user records.
- [ ] Inspect desktop and mobile screenshots, keyboard navigation, empty/error states and filtered/unfiltered layout positions. Fix failures and rerun affected tests.
- [ ] Record verified results and remaining monitoring limitations. The Windows agent, policies and update execution need a separate approved design; do not report them implemented in this delivery.

## Execution handoff

Recommended: native execution in this session. Tasks share DTO, rich-node and revision contracts; sequential implementation avoids competing edits. Review and approve this plan and select native execution or subagent-driven execution before product implementation.
