# Company Inventory Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give Brie private company workspaces, role-controlled access, employee/location records and durable asset history without losing existing data or changing the approved visual style.

**Architecture:** Retain ASP.NET, PostgreSQL and React. Resolve company and actor from authenticated server sessions, pass context explicitly into repositories, and use company-scoped relationships throughout inventory, documentation and chat. Deliver this foundation before enabling the remaining operational tabs.

**Tech Stack:** Existing .NET 10, Npgsql 10, PostgreSQL/pgvector, React 19, TypeScript, Vite, xUnit and Playwright. Add the ASP.NET OpenID Connect package matching the installed .NET servicing version when implementing authentication; no new frontend component framework.

**Spec:** ../specs/2026-10-08-mainstream-inventory-design.md (approved by the user).

## Global Constraints

- Work remains in Clary-Lab; SourceDesk remains untouched.
- Fixed asset types remain Computer, Printer, Monitor, Network and Accessory; categories are company-defined.
- Keep the sidebar order Overview, Inventory, Status, Reports, Kits, Requests.
- Summary cards stay neutral with black icons and no colored icon circles.
- Charts use brighter blue (#3b82f6), green (#22c55e), amber (#f59e0b) and purple (#a855f7); department colors come from company settings.
- Tooltips appear only during pointer hover and disappear immediately on pointer exit, including after clicking.
- Tables expand with the available panel width.
- Software status remains Not checked and Last seen remains empty until actual monitoring data exists.
- Preserve existing IDs, prose, uploaded files and relationships. Never print credentials or place backups in source control.
- Production identity-provider selection and public deployment are outside this implementation. Production without valid identity configuration fails closed.
- There is no Git metadata in this checkout. Record completed tasks and verification in artifacts/inventory-foundation-ledger.txt instead of fabricating commits or initializing Git.

## Review Focus

1. Company switching during a delayed request must not display another company's late response; Tasks 7 and 8 test cancellation and state reset.
2. Duplicate employee names must not silently assign the wrong person; Task 5 tests stable IDs and ambiguous legacy mapping.
3. A department rename must retain chart color, employee membership and old audit wording; Tasks 5 and 6 test these separately.
4. A revoked membership must lose access even with an existing cookie or in-memory chat; Tasks 2 and 4 test per-request membership checks.
5. An interrupted migration or duplicate startup must leave either the old usable state or a single completed migration; Task 1 tests rollback and locking.

## Scope and file map

This plan implements the foundation section of the approved roadmap. Status/checkout, Reports, Kits/Requests/Tickets, drivers/software and operational production rollout remain subsequent independently specified plans. They are required roadmap work, not claims of completion in this phase.

New backend units: Infrastructure/DatabaseMigrations.cs; Identity/{CompanyContext,IdentityEndpoints,CompanyAccess,CompanyRepository}.cs; Directory/{DirectoryContracts,DirectoryRepository,DirectoryEndpoints}.cs; Inventory/AuditRepository.cs; Knowledge/CompanyFiles.cs; scripts/Initialize-Company.ps1. New migrations: database/migrations/002-company-foundation.sql and 003-asset-directory.sql. Existing repositories and endpoint mappings are modified in place for explicit context, without unrelated AI refactoring.

New frontend units: companyApi.ts, CompanySession.tsx, directoryApi.ts, DirectorySettings.tsx, MembershipSettings.tsx and AssetHistory.tsx. Extend existing App.tsx, InventorySettings.tsx, ComputerAssetEditor.tsx, inventoryApi.ts and shared request helpers. Reuse existing AssetCategoryInput.tsx, TableColumnFilter.tsx and CSS.

## Shared interfaces

Define in Identity/CompanyContext.cs:
`enum CompanyRole { Admin, Technician, Manager, Employee }`
`record CompanyContext(Guid CompanyId, Guid UserId, CompanyRole Role, Guid? EmployeeId, Guid? DepartmentId)`.
No repository creates a context from body/header company IDs. CompanyAccess.ResolveAsync(ClaimsPrincipal principal, Guid companyId, CancellationToken ct) returns CompanyContext? from an active membership; null means denied.

DirectoryContracts.cs defines DepartmentDto(Id,Name,Color,Active,Revision), EmployeeDto(Id,DisplayName,Email,DepartmentId,Active,Revision), LocationDto(Id,Name,ParentId,Active,Revision), DirectoryPage<T>(Items,NextCursor). IDs are Guid, revision is int, optional fields nullable. Lists accept query text, opaque cursor and limit 1..100 (default 50). Mutations require expected revision on update; missing/inaccessible resources return 404, stale revision 409, malformed input 400, unauthorized session 401, insufficient role 403.

AuditRepository.AppendAsync(NpgsqlConnection connection,NpgsqlTransaction transaction,CompanyContext context,string action,string resourceType,Guid resourceId,object? before,object? after,CancellationToken ct) writes in the caller's transaction. Audit events are returned by a company/visibility-scoped paginated reader.

## Task 1: Versioned migration and legacy company preservation

**Files:** Create Infrastructure/DatabaseMigrations.cs and database/migrations/002-company-foundation.sql; modify backend/Clary.Api/Program.cs, backend/Clary.Api/Clary.Api.csproj and database/001-initial.sql; test backend/Clary.Tests/CompanyMigrationTests.cs.

**Interfaces:** Produce DatabaseMigrations.ApplyAsync(NpgsqlDataSource database,CancellationToken ct). Existing initial schema remains the bootstrap source; subsequent numbered migrations have checksums and run once under a database advisory lock.

- [ ] Write migration tests named LegacyDataKeepsIdsAndRelations, RepeatedStartupIsIdempotent and FailedMigrationRollsBack. Seed assets/documents/passages/folders/chat projects/pinned folders/shares/conversations; assert original IDs/text/counts remain and company ownership is non-null after migration. Inject a failing statement and assert no ledger entry or half-created company.
- [ ] Run `dotnet test Clary.slnx --filter FullyQualifiedName~CompanyMigrationTests`; confirm new behavior fails before implementation.
- [ ] Implement migration ledger, transaction and advisory lock. Create companies, users, memberships and tenant ownership columns. Add company-scoped keys/FKs to all existing owned tables. Change normalized asset-tag and department-name uniqueness to company-scoped constraints; change inventory_documentation primary key to (company_id,category). Do not leave the original global constraints blocking legitimate duplicates between companies.
- [ ] Include owned file metadata for existing wiki image IDs and authenticated file ownership. Discover existing upload storage references from code before mapping; unmapped image files are quarantined as inaccessible, not publicly served or deleted.
- [ ] Run tests again; assert equal tags/department names in two companies succeed and cross-company child references fail. Record results in the ledger. Do not run on the user's database yet.

## Task 2: Authenticated sessions and company permissions

**Files:** Create Identity/CompanyContext.cs, Identity/CompanyAccess.cs, Identity/IdentityEndpoints.cs and Identity/CompanyRepository.cs; modify Program.cs and Clary.Api.csproj; test backend/Clary.Tests/CompanyAccessTests.cs and CompanySessionTests.cs.

**Interfaces:** Produce CompanyAccess.ResolveAsync as defined above; GET /api/session returns user, active company, memberships and role; POST /api/session/company selects an active membership using an anti-forgery token. GET /api/session/antiforgery issues that token; POST /api/session/logout signs out. GET /api/session/login challenges configured OIDC. Expose permissions as named server policies, not client role strings alone. CompanyRepository also provides GET/PUT /api/company (name, revision) and Admin-only GET/POST/PUT /api/company/memberships (user subject, issuer, role, active, employeeId, revision); every update checks revision and last-Admin protection. OIDC callbacks persist issuer/subject identity without automatically creating a company membership. Membership subject entries may be provisioned by an Admin before first login.

- [ ] Write tests ProductionRejectsDevelopmentIdentity, BodyCannotSelectUnauthorizedCompany, RevokedMembershipRejectsExistingSession, UnsafeRequestsRequireAntiforgery and LastAdministratorCannotBeRemoved. Assert 401/403 and no mutation for denied requests.
- [ ] Run the two filtered test classes and confirm failures.
- [ ] Configure secure HTTP-only session cookies, OIDC validation and request-time active membership lookup. Development identity is explicitly enabled only in Development using a preconfigured bootstrap subject; it cannot read identity/company values from arbitrary headers. Bind the local development server to loopback. Allow only configured frontend origins with credentials; never wildcard credentialed CORS.
- [ ] Map policies: Admin manages memberships; Admin/Technician mutate inventory/directories; Manager reads company inventory and later team requests; Employee reads own assignments only. For now restrict company documentation/chat to Admin/Technician/Manager because no per-document employee grant exists. Employee requests to those resources are denied rather than exposing the full knowledge base.
- [ ] Run tests and verify production startup reports missing authentication configuration without exposing secrets. Record results.

## Task 3: Company-scoped inventory and documentation

**Files:** Modify Inventory/InventoryRepository.cs, Inventory/InventoryDepartments.cs, Inventory/InventoryEndpoints.cs, Knowledge/InventoryDocumentation.cs, Knowledge/LibraryRepository.cs, Knowledge/LibraryOperations.cs, Knowledge/UploadService.cs, Knowledge/KnowledgeRepository.cs, Knowledge/SampleLoader.cs, Knowledge/EpsonModels.cs and Program.cs; create backend/Clary.Tests/CompanyKnowledgeIsolationTests.cs and CompanyInventoryIsolationTests.cs.

**Interfaces:** Add CompanyContext context as the first parameter to every public business read/write operation. Preserve existing response fields; optional new IDs are additive. Endpoints resolve context before invoking repositories. Explicit local sample loading uses the legacy company context. Startup model-metadata repair enumerates companies through an administrative maintenance entry point and invokes a scoped repair for each; it must not become a globally callable business endpoint.

- [ ] Seed A/B with matching tags and titles; write tests OtherCompanyIdsAreInvisible, SearchOnlyReturnsCurrentCompany, ApprovalAndMoveCannotCrossCompanies and EmployeeOnlySeesOwnAssets. Assert foreign IDs return 404, list/search/citations contain no foreign text and denied mutations change no row.
- [ ] Run filtered tests; confirm failures.
- [ ] Scope every SQL read/write, recursive folder lookup, document revision resolution, embedding retrieval, approval and inventory document mapping. Include company in automatic-document locking semantics and uniqueness. Do not accept caller-provided document/employee IDs without checking ownership. Employee live-document endpoints are denied per Task 2.
- [ ] Remove public image serving and route image/file access through CompanyFiles.OpenAsync(CompanyContext context,Guid fileId,CancellationToken ct), which returns an owned file descriptor or null. Require authentication even for raw image URLs.
- [ ] Run new isolation tests and existing inventory/document/library/retrieval integration tests; record results and update isolated fixtures to supply explicit contexts.

## Task 4: Company-scoped chat state, shares and uploads

**Files:** Modify Knowledge/ConversationRepository.cs, Knowledge/UploadService.cs, Program.cs, Contracts.cs and Ai/ConversationStore.cs; create Knowledge/CompanyFiles.cs and backend/Clary.Tests/CompanyConversationIsolationTests.cs.

**Interfaces:** Conversation cache keys become (CompanyId,UserId,ConversationId); ask/load/action methods accept CompanyContext. Shares require authenticated company membership and original visibility permission. CompanyFiles uses database ownership records, never storage-path input.

- [ ] Write tests SameConversationIdDoesNotReuseForeignCache, RevokedUserCannotContinueCachedChat, ForeignShareTokenIsDenied and UploadRollbackLeavesNoReadableFile. Assert foreign turns/citations never enter responses and orphaned uploads are inaccessible.
- [ ] Run filtered tests; confirm failures.
- [ ] Pass company context through ask/stream/retry/project/pinned-folder/share routes and retrieval callbacks. Partition mutable cached state by company and actor; revalidate membership on each endpoint, including streaming setup. Ensure every upload/image path has owner metadata and cleanup on failed persistence. Existing share links now require access, as specified.
- [ ] Run conversation/upload tests plus isolation tests. Inspect every /api mapping in Program.cs: only minimal health and authentication entry points may be anonymous. Record the endpoint inventory and results.

## Task 5: Employee, department and location directories

**Files:** Create database/migrations/003-asset-directory.sql and Directory/{DirectoryContracts,DirectoryRepository,DirectoryEndpoints}.cs; modify Inventory/InventoryDepartments.cs and Program.cs; test backend/Clary.Tests/DirectoryIntegrationTests.cs.

**Interfaces:** GET/POST /api/directory/employees, departments, locations; PUT /api/directory/{kind}/{id}; all consume shared DTO/revision rules. DirectoryRepository.ListEmployeesAsync(context,query,cursor,limit,ct) returns DirectoryPage<EmployeeDto>; equivalent methods for departments/locations. Mutations return the saved DTO. Updates set Active=false for deactivation; no destructive delete endpoint.

- [ ] Write tests LegacyAmbiguousNamesRemainUnresolved, DuplicateNamesHaveDistinctIds, DepartmentRenamePreservesColorAndRelations, InactiveEntryRemainsOnOldAssets, LocationCannotParentItselfOrDescendant and DirectoryPaginationIsStable. Assert trimmed names, deterministic cursor ordering by name/id, company boundary and stale-update 409.
- [ ] Run filtered tests and confirm failures.
- [ ] Create directory IDs and asset reference columns while retaining text snapshots. Match legacy names only where unambiguous; record unresolved entries for Settings review. Validate names 1..100, email <=254 when provided, department colors six-digit hex and location hierarchy within company. Existing department endpoints adapt to the new service during frontend transition.
- [ ] Run tests and existing department-color tests; record results.

## Task 6: Transactional assignments and immutable audit

**Files:** Create Inventory/AuditRepository.cs; modify Inventory/InventoryRepository.cs and Inventory/InventoryEndpoints.cs; create backend/Clary.Tests/InventoryAuditTests.cs and InventoryDirectoryTests.cs.

**Interfaces:** AssetInput adds EmployeeId, DepartmentId, LocationId (nullable Guid) and NewAssigneeName (nullable string); existing display fields remain in responses. POST/PUT asset requires directory IDs for selected options. NewAssigneeName creates an employee only when no EmployeeId is supplied; identical names trigger an explicit duplicate-choice 409 rather than silently merging. GET /api/inventory/assets/{id}/history returns scoped events with cursor.

- [ ] Write tests AssetAndAuditCommitTogether, StaleAssetDoesNotCreateEmployeeOrAudit, ForeignDirectoryIdsAreRejected, DuplicateAssigneeRequiresExplicitChoice and RenamingDepartmentDoesNotRewriteAudit. Assert failed transaction produces no employee/asset/audit residue.
- [ ] Run filtered tests; confirm failures.
- [ ] Implement atomic employee creation, asset reference validation, optimistic update and audit append. Audit records actor/time and before/after snapshots; never credentials. Apply the audit helper to directory/company/membership mutations. Asset reads join current directory names while old audit text stays immutable. Enforce Employee own-asset visibility on history too.
- [ ] Run tests and inventory integration tests; record results.

## Task 7: Session-aware frontend without design changes

**Files:** Create frontend/src/companyApi.ts and CompanySession.tsx; modify App.tsx, inventoryApi.ts, and all document/chat/file request helpers identified with `rg -n 'fetch\(' frontend/src`; create frontend/tests/company-session.spec.ts.

**Interfaces:** companyApi exports getSession(signal?), selectCompany(id), logout() and authenticatedFetch(input,init?). CompanySession provides session, permissions, switchCompany and reloadSession. authenticatedFetch adds cookies and anti-forgery headers to mutations; 401 displays session recovery rather than pretending the inventory is empty.

- [ ] Write browser tests LoginFailureKeepsRecoveryVisible, CompanySwitchClearsVisibleData, DelayedOldCompanyResponseIsIgnored and EmployeeCannotSeeAdminControls. Mock API permissions and a deferred A response; after switching B assert no A row appears.
- [ ] Run the browser file; confirm failures.
- [ ] Add session loading/recovery using existing panels and buttons. Cancel requests and clear company-owned UI/cache state on switch/logout; key company views by CompanyId and reject late results from prior generations. Preserve existing top tabs and icon rail. Update existing browser mocks to provide the development session contract.
- [ ] Run build and browser test; record results.

## Task 8: Settings directories and aligned asset dropdowns

**Files:** Create frontend/src/directoryApi.ts, DirectorySettings.tsx and MembershipSettings.tsx; modify InventorySettings.tsx, ComputerAssetEditor.tsx, inventoryApi.ts and existing inventory-overview.css; create frontend/tests/inventory-directories.spec.ts.

**Interfaces:** directoryApi exports paginated list/save methods matching Task 5; asset save passes stable IDs. Settings sections: Company, Departments, Employees, Locations, Memberships, gated by server permissions. Membership management references an already-known OIDC user subject; do not invent email delivery/invitation functionality in this phase.

- [ ] Write browser tests PairedControlsHaveEqualHeight, DuplicateAssigneeShowsDepartmentAndEmail, InactiveAssigneeRemainsOnEdit, DirectoryFailurePreservesDraft and SettingsCompanySwitchDropsLateResults. Assert geometry within one CSS pixel and the approved dropdown structure. Cover Task 7's delayed-switch case in Settings independently.
- [ ] Run tests; confirm failures.
- [ ] Reuse AssetCategoryInput appearance and keyboard behavior, extending option labels/IDs without replacing the component framework. Employee options show secondary identifying text; creating a name prompts duplicate resolution when the API returns 409. Preserve Enter-to-save, required fixed type/status and custom category typing. Add department rename/deactivate with color retention, employee/location forms and Admin membership controls; show API errors without closing drafts.
- [ ] Run directory, department, category and asset browser tests plus production build. Inspect desktop/mobile screenshots against the approved style; record results.

## Task 9: Asset history and existing-data reliability

**Files:** Create frontend/src/AssetHistory.tsx; modify ComputerAssetDetails.tsx, Inventory.tsx, InventoryDocumentationLink.tsx and Knowledge/InventoryDocumentation.cs; test backend/Clary.Tests/InventoryDocumentationTests.cs and frontend/tests/inventory-history.spec.ts, inventory-loading.spec.ts.

**Interfaces:** AssetHistory({assetId:string}) consumes Task 6 history endpoint. Documentation health returns a repairRequired flag; POST /api/inventory/documentation/repair restores the matching live block through a new document revision while preserving prose and uses the existing company authorization.

- [ ] Write tests ActivityFailureDoesNotHideAssets, AssetsFailureDoesNotShowZeroCounts, SwitchingTypeClearsOldDocumentLink, MissingLiveBlockRepairPreservesProse and EmployeeHistoryCannotOpenForeignAsset. Assert explicit errors and no stale document navigation.
- [ ] Run tests and confirm failures.
- [ ] Separate inventory/activity loading results, reset link state immediately when type changes and validate the mapped document's live block. Add a visible repair action rather than silently overwriting prose. Add paginated history within the existing details panel, showing actor/action/date and changed fields with vertically aligned cells.
- [ ] Run focused tests and existing documentation-width/filter/tooltip regressions; record results.

## Task 10: Bootstrap, preservation rehearsal and release verification

**Files:** Create scripts/Initialize-Company.ps1 and docs/company-foundation-operations.md; update scripts/Start-Clary.ps1, LAB-README.md and artifacts/inventory-foundation-ledger.txt; test backend/Clary.Tests/CompanyBootstrapTests.cs.

**Interfaces:** Initialize-Company.ps1 accepts CompanyName, IdentitySubject and Issuer (not a plaintext password), creates the initial Admin through a local authenticated database connection and is idempotent for the same subject/company. No network-accessible bootstrap endpoint. Existing production membership policy prevents removing the last Admin.

- [ ] Write bootstrap tests asserting duplicate invocation creates one membership, unknown/malformed issuer is rejected and transaction failures leave no partial user/company. Run to confirm failures, implement, then rerun.
- [ ] Document local Development configuration versus production OIDC configuration, backup/file ownership migration and rollback. Keep credentials out of logs and command examples. Rehearse backup/restore and migration against an isolated copy before touching the real lab database; verify asset/document/conversation IDs, counts and file links.
- [ ] Run backend suite using CLARY_TEST_CONNECTION without printing its value, `npm run build`, then relevant company/directory/history tests and existing overview/assets/category/departments/documentation/filter/tooltip/suite-navigation regressions. A missing integration database is a verification blocker, not a passing test.
- [ ] After successful rehearsal, back up the actual lab database/files, apply migrations and bootstrap the local identity using the existing local administrative setup. Restart only Clary-Lab. Smoke-test actual saved assets and documents read-only; do not create synthetic user inventory records.
- [ ] Perform a whole-change code review focused on unscoped SQL, anonymous routes, cache keys, file ownership, role negatives and data preservation. Resolve findings and rerun affected checks. Record exact commands/results and any outstanding production identity configuration.

## Self-review and execution handoff

Coverage: Tasks 1-4 cover company boundaries and all existing resource paths; 5-6 cover directories, migration compatibility and audit; 7-9 preserve the UI and repair specified integration weaknesses; 10 covers bootstrap and preservation verification. Later roadmap phases are explicitly outside this foundation plan and remain required subsequent work. Five Review Focus conditions each have an owning test. Shared CompanyContext/DTO/audit interfaces are consistent across tasks.

Review this saved plan before implementation and select Native or Subagent-driven execution. Native means the current agent implements each task and obtains a final independent review. Subagent-driven means separate implementer/reviewer contexts for each task; it costs more but is recommended here because cross-company leakage is expensive and the foundation touches every existing repository.
