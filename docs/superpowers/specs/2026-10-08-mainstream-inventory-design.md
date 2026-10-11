# Mainstream inventory roadmap and foundation design

Date: 2026-10-08
Status: Written specification for user review; product implementation has not started.
Project: C:/Projects/Apps/Clary-Lab (Brie by UNIO).

## Intent and constraints

Extend the working inventory into a product usable by different companies, finish Status, Reports, Kits and Requests, and preserve existing assets, documents and conversations. Follow the already approved website design. Work remains in Clary-Lab; SourceDesk remains untouched.

Working assumption: a hosted service with private company workspaces, while retaining local development. Public hosting, purchasing services and selecting a production identity provider are separate deployment decisions. This document defines the complete roadmap and the first independently deliverable foundation. Later phases receive detailed specifications before implementation; their inclusion here does not mean their interfaces are already implemented.

## Approved visual contract

Keep the off-white shell, rounded white panels, thin neutral borders, existing typography and compact spacing. Use black outline icons and the softer charcoal Settings gear above the profile. Summary cards stay neutral with black icons and no colored icon circles. Charts use brighter blue (#3b82f6), green (#22c55e), amber (#f59e0b) and purple (#a855f7); department colors come from company settings.

Keep the sidebar order Overview, Inventory, Status, Reports, Kits, Requests. Inventory includes all assets. Reuse the existing custom searchable dropdown appearance, white rounded option panels and focus styling. Inputs in paired columns have equal height and aligned labels. Table cells are vertically centered with equal top and bottom padding; long headers truncate with an ellipsis and retain their filter button. Tables expand with the available panel width. Tooltips appear only during pointer hover and disappear immediately on pointer exit, including after clicking.

Asset creation stays in Inventory through the black plus button beside the table heading. Preserve Enter-to-save and keyboard dropdown selection. Fixed asset types remain Computer, Printer, Monitor, Network and Accessory; categories are company-defined. Assignees support selecting previous employees and entering a new employee. Departments come from Settings. Website-managed inventory documents remain full width without the document outline sidebar; ordinary documents retain their normal layout.

## Current baseline

The application has persisted assets, departments and colors, an Overview, editable asset forms, table filters and automatic type-specific inventory documents. Status, Reports, Kits and Requests are navigation placeholders. Employee and location values are currently text. Software status remains Not checked and Last seen remains empty until actual monitoring data exists. There is no company isolation or end-user authentication yet. Existing behavior and data must survive migration.

## Roadmap and delivery order

1. Company foundation: identity boundaries, company memberships and roles, employee/location directories, tenant-safe migrations and immutable audit history.
2. Inventory operations and Status: checkout, return, acknowledgments, due dates, lifecycle actions, vendor/purchase/warranty records, maintenance schedules, typed custom fields, spreadsheet import/export and authenticated QR labels.
3. Reports: persisted report filters, department/type/category/status breakdowns, overdue returns, warranty and maintenance views, purchase totals grouped by currency, and downloadable CSV/XLSX reports.
4. Kits and Requests: reusable department/role equipment requirements, availability checks, approvals, reservations and fulfillment, plus a persisted Tickets integration for onboarding requests.
5. Drivers and software: shared driver library, per-model/asset links, authenticated downloads, license records, real monitoring check-ins and staged software update jobs.
6. Operational readiness: backup/restore verification, migration rehearsal, deployment configuration and complete regression review.

Each phase must provide real persistence, useful empty/loading/error states and tests before being described as complete. Navigation is enabled when its corresponding workflow is usable.

## Foundation architecture

Continue with the existing ASP.NET API, PostgreSQL database and React frontend. Add focused company, identity, people, locations and audit services; repositories receive a server-resolved CompanyContext rather than trusting a company identifier supplied in a request body. Business endpoints require authenticated membership and explicit permission checks.

Use an identity adapter boundary that accepts a validated external subject and resolves it to an application user and membership. Production authentication uses configured OpenID Connect with secure HTTP-only session cookies and anti-forgery protection for mutations. No production identity provider is selected by this specification. A development identity adapter may be enabled only by explicit development configuration, must fail closed outside Development, and must never accept arbitrary company selection from an untrusted header.

A company owns its resources. Scope assets, departments, employees, locations, vendors, tickets, kits, requests, drivers, software records, audit events, documents, passages/embeddings, library folders, uploads, conversations, projects, shares and export jobs. Existing sharing behavior must not bypass company membership. All resource lookups, search, AI retrieval, file downloads, exports and background jobs enforce the same company boundary. Use company-scoped unique keys and composite foreign keys where possible so cross-company relationships cannot be persisted accidentally. Database access helpers require the company context; negative integration tests verify that guessed IDs do not reveal another company's data.

## Foundation data and migration

Add companies, users and company_memberships. Memberships have Admin, Technician, Manager or Employee roles. Add employees and locations independently of login accounts: an employee can hold equipment without logging in. Employees carry display name, optional email and department; locations carry a name and optional parent for site/room/storage hierarchy. Department uniqueness becomes company-scoped and case-insensitive.

Run versioned, transactional migrations. Assign existing records to a single legacy company while preserving IDs and relationships. Bootstrap its first administrator through an explicit local administrative command rather than an open registration endpoint. Back up the lab database before migration and verify counts and relationship integrity afterward. Do not silently drop columns or discard text during the migration.

Add employee_id, department_id and location_id to assets while preserving legacy text for compatibility and historical display. Match unambiguous normalized existing values into directory entries within the legacy company. Ambiguous matches remain visible for administrator resolution. New asset edits use directory IDs; entering a new assignee creates an employee in the same transaction after duplicate checks. Deactivated directory entries stay visible on historical records but are unavailable for new assignments. Department renaming preserves its ID, chart color and asset relationships.

Audit events contain company, actor, action, resource type/ID, timestamp and relevant before/after values. Write the event in the same transaction as the mutation. Audit history has no ordinary edit/delete endpoint and excludes credentials, session tokens and uploaded binary content. Existing inventory_activity remains readable while new actions use the richer audit source.

## Permissions

Admin manages company settings, memberships and all inventory workflows. Technician manages assets, directories, maintenance and fulfillment, but cannot change memberships. Manager reads company inventory, manages their team's requests and approves requests for their department. Employee sees their own assignments and requests and can acknowledge handovers or submit requests. Reports, exports and document retrieval apply these visibility rules, not merely sidebar hiding. Requests beyond a user's scope return a consistent inaccessible response without disclosing ownership.

Foundation Settings provides company details, Departments with colors, Employees, Locations and Memberships. Reuse existing panel and form patterns. Keep charts and existing asset forms functioning during migration.

## Remaining feature contracts

### Status and inventory operations

Status is a filtered operational view of Assigned, Available, In repair and Retired assets, including overdue returns and maintenance due. Checkout records employee, issue date, due date and optional acknowledgment; return records date, condition and receiving technician. Transactional checks prevent simultaneous assignment and reject invalid lifecycle transitions. Assignment history remains available after return or retirement.

Store vendor, purchase date, purchase amount/currency, warranty end date and maintenance records. Custom fields have a company-defined label, type, applicable asset types and validation; support text, number, date, boolean and choice values. Spreadsheet import supports CSV and XLSX through upload, column mapping, preview, validation and explicit confirmation. Preview changes nothing; rejected rows and duplicate resolution are visible. Export respects current filters and permissions. QR labels contain an opaque asset link and require normal authentication to view details.

### Reports

Reports query persisted inventory facts with date, department, location, type, category and status filters. Provide inventory distribution, assignment/return history, overdue equipment, maintenance/warranty and purchases. Never add amounts across different currencies without an explicit conversion source. Downloads use the same permission scope as the on-screen report and record an audit event.

### Kits, Requests and Tickets

A kit defines equipment type/category quantities and optional software requirements for a department or role. Requests identify an employee, kit or individual items, needed date and related ticket. States are Draft, Pending approval, Approved, Partially fulfilled, Fulfilled and Cancelled. Approval and fulfillment are separate permissions/actions.

Approval reserves available compatible assets transactionally; shortages stay visible and cannot be reported as fulfilled. Fulfillment assigns reserved equipment and records handovers. Cancellation releases reservations without erasing history. Concurrency checks prevent two requests reserving the same asset. Onboarding tickets create a linked request idempotently using the employee's department/role kit; automatic assignment occurs only where the company's approval policy permits it and matching assets are available. Persist ticket/request relationships and retryable integration events; repeated delivery cannot duplicate requests or assignments.

### Drivers and software

Provide a shared driver library with name, vendor/model, version, operating system, architecture and optional official source link. Uploads use generated storage identifiers, configured size/type limits and checksums. Associate a driver with multiple models or assets; download through an authenticated attachment endpoint. Never execute installer uploads on the server or index their binary contents as knowledge text.

Track software products, license quantities, assignments and expiry. Monitoring requires explicit device enrollment and revocable credentials; record actual check-in timestamps and reported software inventory. Distinguish Not checked, stale data and checked results. Update jobs require an enrolled agent with supported capabilities, company policy and explicit approval; show queued/running/succeeded/failed results from real agent reports. No fake success status or arbitrary remote-shell feature.

### Operational readiness

Provide documented database and file backups and a tested restore procedure. Treat restore as an administrative operation; verify asset/document/file relationships after recovery. Production deployment requires configured identity, secure transport, secrets management and a successful isolation review. Development remains usable without provisioning a public service.

## Error handling and integration requirements

Preserve optimistic revision checks and return a conflict when an asset changes during editing. Keep drafts open after recoverable failures. Isolate overview/activity/directory loading so one failure does not present false zero counts or hide available inventory. Clear stale documentation-link state when asset type changes. Verify the live inventory block exists before reporting an automatic document as healthy; offer a repair that preserves surrounding document content if it is missing. Automatic folder/document creation remains idempotent and transactional with the first asset of each type.

Use bounded pagination for directories, history and reports. Every background job records company and initiating actor and revalidates access when producing a result. Files and export results are never served by guessing an unscoped storage path.

## Foundation acceptance and verification

- Existing assets, department colors, documents, folders, conversations and links retain their IDs and remain usable after migration.
- A user in company A cannot read, mutate, search, export, download or retrieve AI passages belonging to company B, including through guessed IDs and background jobs.
- Every role has positive and negative API authorization coverage; hiding UI controls alone is insufficient.
- Employee/location creation, selection, deactivation and reload persist; ambiguous legacy text is retained for resolution.
- An asset mutation and its audit event commit together; failed/stale edits create neither a partial change nor a success event.
- Existing automatic inventory documentation, filters, Enter-to-save, chart drilldowns and department colors still work.
- Browser checks cover matched dropdowns, aligned controls/cells, responsive tables, instant tooltip dismissal and full-width live documents.
- Run backend tests, frontend production build and relevant browser regressions. Use isolated test company/schema data and never alter the user's real assets for test fixtures.

## Review boundary

This specification records the complete requested roadmap and a concrete company foundation. After written-spec review, prepare the foundation implementation plan with exact migrations, endpoints, UI changes and verification steps. Subsequent phase specifications refine their feature contracts without changing the approved visual language. No public deployment or production provider provisioning is implied.

Self-review: checked for placeholders, conflicting permission/data rules, preservation of existing data, design consistency and separation of roadmap from the first implementation phase. Repository has no Git metadata, so this specification is saved without a commit.
