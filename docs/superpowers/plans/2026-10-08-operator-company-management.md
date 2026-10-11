# Operator and Company Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the sole platform Operator a private account-management dashboard and enforce company roles, subscriptions, reserved invitations and access revocation.

**Architecture:** Extend the existing PostgreSQL company identity system and secure cookie/OIDC authentication. Platform authority is a singleton identity binding, separate from company membership. Company row locks serialize seat changes; request-time access checks enforce revocation and tenant boundaries.

**Tech Stack:** ASP.NET Core 10, Npgsql/PostgreSQL, React/TypeScript, Vite, Playwright, xUnit.

**Spec:** `docs/superpowers/specs/2026-10-08-operator-company-management-design.md` (approved 2026-10-08).

## Global Constraints

- Work remains in Clary-Lab; SourceDesk remains untouched.
- Preserve existing IDs, prose, uploaded files and relationships. Never print credentials or place backups in source control.
- Reuse existing secure HttpOnly cookie/OIDC authentication. No public registration, promotion or Operator assignment endpoint.
- Bind the Operator to validated issuer and subject, never an email comparison or client claim. Until configured, Operator operations fail closed.
- A non-Operator user belongs to one company. Platform authority grants no implicit access to ordinary company content.
- Administrator assignment and subscription changes belong only to Operator. Administrators manage only Editors and Viewers; no self role/company changes.
- Editor deletion is denied by default; Viewer cannot create, edit, delete, approve, index, repair or modify settings.
- Custom requires an integer greater than100; Unlimited remains distinct and has no numeric cap.
- Enabled accounts plus pending, unexpired, unrevoked invitations consume seats. Administrator counts. Disabled accounts do not.
- Online means authenticated non-expired session activity within five minutes; count distinct users. Invitations expire after seven days.
- Use the website's off-white shell, white rounded panels, thin neutral borders, outline icons, black primary actions and existing custom dropdowns.
- Login uses the approved light split panel with soft warm gradient. No invented password/social sign-in controls.
- No Git metadata: retain snapshot review packages and ledger; do not initialize Git or fabricate commits.
- No live synthetic uploads/inventory. Isolated browser fixtures must block unmocked API traffic.

## Review Focus

- Two simultaneous last-seat changes: exactly one succeeds, including mixed create/invite/reactivate operations (Task3).
- Expired/replayed invitations and identity email mismatch: no stale reservation or unauthorized acceptance (Task3).
- Revocation between session load and mutation: request-time checks and transactional revalidation deny the mutation (Tasks2–4).
- Legacy multi-company memberships or role mappings: rehearsal reports conflicts and preserves visibility without broadening access (Task1).
- Slow responses after sign-out/suspension: stale data cannot repopulate the UI or presence (Tasks5–6).

## File and interface map

Existing `Identity/CompanyAccess.cs`, `IdentityEndpoints.cs` and `CompanyRepository.cs` own sign-in and company context. Extract account administration into `Accounts/` rather than growing those files. Existing `Inventory/AuditRepository.cs` remains the company inventory audit; add platform account audit independently. New migrations append after005; never modify previous migration checksums.

Shared contracts in `Accounts/AccountContracts.cs`:

- `CompanyRole { Administrator, Editor, Viewer }`; `CompanyContext` additionally carries an explicit company visibility scope retaining legacy assigned-only access.
- `OperatorContext(Guid UserId)` is resolved from the authenticated principal, not request data.
- `PlanKind { Ten, Twenty, Fifty, Hundred, Custom, Unlimited }` and `SubscriptionInput(PlanKind Kind, int? CustomLimit, int? Revision)`.
- `SeatUsage(int Enabled, int Pending, int? Limit, int? Remaining, bool OverLimit)`; null Limit/Remaining means Unlimited.
- `AccountDto(Guid Id,string DisplayName,string? Email,CompanyRole Role,bool Enabled,int Revision,DateTimeOffset? LastActivity,bool Online)`.
- `CompanyAdminDto(Guid Id,string Name,bool Active,int Revision,PlanKind Plan,int? Limit,SeatUsage Seats,int OnlineUsers,DateTimeOffset? LastActivity,bool NeedsAdministrator)`.
- Paged results `{items,nextCursor}` use opaque scope/query-bound cursors, default50/max100.
- Session response retains user/activeCompany and adds `isOperator` plus explicit capabilities; ordinary users have one membership.
- HTTP401 unauthenticated,403 unauthorized,404 inaccessible target,400 malformed input,409 revision/seat/invariant conflict. Seat errors include stable code `seat_limit_reached`, usage and limit.

## Task1: Schema, role migration and sole-owner bootstrap

**Files:** Create `database/migrations/006-account-management.sql`, `Accounts/AccountContracts.cs`, `Accounts/OperatorAccess.cs`, `Accounts/OperatorBootstrap.cs`, `scripts/Initialize-Operator.ps1`; modify `Identity/CompanyContext.cs` and role consumers/fixtures for compilation; tests `OperatorBootstrapTests.cs`, `AccountMigrationTests.cs`.

**Interfaces:** `OperatorAccess.ResolveAsync(ClaimsPrincipal,CancellationToken) -> Task<OperatorContext?>`; local bootstrap accepts verified Issuer and Subject, never password. Provisioning fails until that pair is supplied; do not use the username/password posted in chat as configuration.

- [ ] Write failing tests: singleton cannot acquire a second owner; repeated same-owner bootstrap idempotent; different owner rejected; migration preserves IDs/file links and maps Admin→Administrator, Technician→Editor, Manager→Viewer, Employee→Viewer with assigned-only scope; multiple company memberships abort/report rather than silently reassign.
- [ ] Run focused xUnit filters and confirm behavioral failures.
- [ ] Append schema for singleton owner, subscription plan/limit/revision, invitations with token hash/expiry/state, session presence/revocation and immutable account audit. Enforce unique company association for ordinary users and at most one enabled Administrator per company. Lock/validate malformed issuer and unsupported plans. Backfill existing companies to Unlimited, preserving current access/seat usage without arbitrarily disabling accounts.
- [ ] Implement local transactional bootstrap, exact issuer/subject binding and active-user checks. Keep prior migrations unchanged. Adapt role references without granting new capabilities; enforcement completed Task2.
- [ ] Run migration/bootstrap tests; record snapshot and report, then independent review.

## Task2: Explicit permissions and immediate access checks

**Files:** Create `Accounts/AccountPermissions.cs`; modify `Identity/CompanyAccess.cs`, `IdentityEndpoints.cs`, `CompanyRepository.cs`, endpoint mappings in `Program.cs`, `Knowledge/CompanyConversationEndpoints.cs`, file and inventory endpoints; tests `AccountPermissionTests.cs`, existing isolation/session tests.

**Interfaces:** Operator policy resolves `OperatorContext` independently of company membership. Company read/write/delete/approve/account-admin capabilities are explicit; no path-prefix fallback grants mutation to Viewer. Existing unscoped repository calls remain prohibited.

- [ ] Write failing actual middleware/HTTP tests covering each role's read/write/delete/approve/index/repair/settings/account permissions; non-owner Operator denial; forged role/company body denial; disabled user and suspended company with existing cookie denied; absent company lookup returns recovery rather than500.
- [ ] Run focused tests and confirm failures.
- [ ] Replace broad old role gates with capabilities. Remove old unrestricted membership creation API behavior. Only Operator can create/replace Administrator; company Administrator may manage only Editor/Viewer and never themselves. Preserve restricted legacy Viewer asset visibility. Require transaction-time revalidation in mutation services after company locks.
- [ ] Keep CSRF, cookie security and OIDC validation. Reject ordinary user's attempt to switch to a different company; Operator cannot use that route as implicit impersonation.
- [ ] Run affected session/isolation/security tests; report and independent review.

## Task3: Company accounts, subscriptions and seat-reserved invitations

**Files:** Create `Accounts/CompanyAccountRepository.cs`, `SeatAccounting.cs`, `InvitationService.cs`, `AccountAuditRepository.cs`, `AccountEndpoints.cs`; modify `Program.cs`; tests `SeatAccountingTests.cs`, `InvitationTests.cs`, `CompanyAccountTests.cs`.

**Interfaces:** `/api/operator/companies` GET/POST, `/{id}` GET/PUT, `/{id}/subscription` PUT, `/{id}/administrator` PUT, `/{id}/accounts` GET/POST, `/{id}/accounts/{userId}` PUT. Own-company `/api/company/accounts` GET/POST and `/{userId}` PUT, `/api/company/seats` GET. Invitations GET/POST/DELETE under both permitted account surfaces; `/api/session/invitations/accept` POST accepts `{token}` after trusted identity validation. New account payload binds issuer/subject plus displayName/email, Editor/Viewer role and enabled status; Operator Administrator assignment is a separate operation. All updates include expected revision.

- [ ] Write failing tests for 10/20/50/100/Custom/Unlimited validation, enabled+pending counts, disabled release, Administrator seat, over-limit reduction preserves users, duplicate invites, stale edits, audit rollback, cross-company and self-change denial.
- [ ] Add concurrent tests: last-seat mixed operations permit exactly one; accepting invitation preserves total usage; replay does not create account twice; expired/revoked invitation frees seat; reissue replaces reservation; mismatched verified email/issuer-subject cannot accept; reused identity cannot join another company.
- [ ] Confirm RED; implement company-row-lock transactions shared by plan edits and every seat-changing writer. Expiry evaluated using database time. Seat accounting uses logical expired state even before cleanup. Accept locks reservation and company consistently, permits reservation conversion when reduced plan is over limit, and does not create a second seat. Existing enabled user invitation rejected rather than duplicated.
- [ ] Create/edit/suspend/reactivate companies; atomic initial Administrator creation; transactional replacement demotes previous Administrator to Editor while preserving enabled status, consuming a seat only if enabling/adding the replacement. Suspension and account deactivation never delete data. Deny deletion of company/account through these controls.
- [ ] Hash random single-use invitation tokens; return copyable link once only, no fake sent-email notification. Acceptance requires provider-verified intended email or explicit bound subject. Audit state transitions without tokens. Revocation/status changes invalidate relevant sessions.
- [ ] Run focused concurrent and endpoint tests; report and independent review.

## Task4: Presence, session revocation and management audit reads

**Files:** Create `Accounts/SessionPresence.cs`; modify identity cookie lifecycle and `AccountEndpoints.cs`; tests `SessionPresenceTests.cs`, `AccountSuspensionTests.cs`.

**Interfaces:** POST `/api/session/heartbeat` updates current trusted session; DELETE semantics are not needed for logout (existing POST logout revokes). GET `/api/operator/audit` and `/api/company/account-audit` paginated readers. Company details/accounts include Task1 presence and usage fields.

- [ ] Write failing tests for distinct-user online counts across sessions, five-minute cutoff, expiry/logout, disabled user/suspended company heartbeat rejection, session after reactivation remains revoked, last-activity preservation and foreign-company audit denial.
- [ ] Confirm RED; issue random session ID on sign-in, persist expiration/revocation and validate on every protected API call. Throttle activity writes without extending cookie lifetime. Mutating operations recheck under lock; avoid caching authority.
- [ ] Implement heartbeat, online/last-activity reads and scoped immutable audit pagination, using server time. Operator's companyless activity does not count as a company's online user.
- [ ] Run focused tests and affected identity suite; report and independent review.

## Task5: Operator dashboard and Administrator account settings

**Files:** Create frontend `accountApi.ts`, `OperatorDashboard.tsx`, `CompanyAccountSettings.tsx`, `SubscriptionControl.tsx`; modify `CompanySession.tsx`, `companyApi.ts`, `App.tsx`, `InventorySettings.tsx`, `MembershipSettings.tsx`, scoped CSS; tests `operator-dashboard.spec.ts`, `company-accounts.spec.ts`.

**Interfaces:** Consume Task3/4 routes and Task1 DTOs through existing authenticatedFetch. Operator dashboard is accessible with no company membership; company workspace remains a separate explicit capability. Replace superseded MembershipSettings.

- [ ] Write isolated browser tests: non-owner has no dashboard and forced route shows denied; owner company create/edit/status/plan/admin/user flows; disabled/pending/online distinguishable; custom vs Unlimited controls; over-limit conflict retains form draft and refreshes seats; Administrator sees own users only and cannot choose Administrator role/change self/subscription.
- [ ] Confirm RED; implement responsive white panels/tables/custom dropdowns, consistent headings/spacing, error/retry/empty/loading states. Display enabled, pending, online and remaining separately, with over-limit and needs-administrator states. Show copy-once invitation link and manual-sharing wording.
- [ ] Replace role UI gates with capabilities, preserve directory management only for Administrator, Editor content editing and Viewer readonly. Fix inherited `.department-add button` selector leaking black background to nested dropdowns. Add visible keyboard-active options.
- [ ] Run build, focused isolated tests and desktop/mobile screenshot inspection; report and independent review.

## Task6: Clean login, invitation acceptance and session-safe UI

**Files:** Create `LoginShell.tsx`, `login.css`, `InvitationAccept.tsx`, `useSessionHeartbeat.ts`; modify `CompanySession.tsx` and `companyApi.ts`; tests `company-session.spec.ts`, `invitation-accept.spec.ts`.

**Interfaces:** Retain secure provider redirect and existing recovery/retry semantics. Heartbeat runs while document visible and stops on logout/access denial; use authenticatedFetch cancellation/generation guards.

- [ ] Add failing tests for companyless Operator entry, invitation token acceptance without token logging/storage, identity mismatch, session revocation and delayed response cannot restore stale company data; responsive login geometry without horizontal overflow.
- [ ] Confirm RED; implement approved centered rounded light split panel, soft warm gradient Brie brand side, compact sign-in area and black primary action. Mobile stacks/hides decoration. No password or registration UI unsupported by authentication.
- [ ] Implement invitation login/accept flow; keep token only in memory as feasible, strip it from URL after capture, no analytics/logging. Visible-tab heartbeat every60seconds, immediate visible resume, canceled on teardown/revocation; no polling when hidden.
- [ ] Run build/session/invitation tests, inspect desktop/mobile screenshots; report and independent review.

## Task7: Finish retained inventory history and reliability

**Files/interfaces:** Carry forward original foundation Task9 brief at `.superpowers/sdd/company-inventory-foundation/task-9-brief.md`, adapting permissions to Task2. Create `AssetHistory.tsx`; separate inventory/activity loading, clear stale documentation links on type switch, validate live block and explicit repair preserving prose through new revision. Existing audit data and stable assignment IDs remain unchanged.

- [ ] Write RED tests for activity failure leaving assets visible, asset failure avoiding false zero counts, type-switch clearing link, repair preserving prose and Viewer foreign/restricted history denial.
- [ ] Implement history pagination with readable actor/action/date/changes; repair only with allowed mutation capability.
- [ ] Run focused backend/frontend history/loading/documentation tests plus width/filter/tooltip regressions; report and independent review.

## Task8: Preservation rehearsal, integration and local rollout

**Files:** `scripts/Start-Clary.ps1`, `scripts/Initialize-Company.ps1`, `docs/company-foundation-operations.md`, `LAB-README.md`, local Vite proxy configuration; tests bootstrap/integration and retained original Task10 verification.

- [ ] Run full backend suite using CLARY_TEST_CONNECTION without printing credentials, frontend build and isolated relevant browser suites. Run most-capable independent whole-change review; one consolidated fix wave and scoped rereview per chosen SDD workflow.
- [ ] Rehearse backup/restore plus all numbered migrations against isolated copy and copied files. Verify IDs/counts/relations/file links, role mapping and multi-company conflict detection. Never rerun legacy001 initialize against migrated schema.
- [ ] Document OIDC configuration, singleton binding, invitation manual delivery, presence meaning, suspension/recovery and backup rollback. Without supplied trusted Operator identity, leave binding unset and report Operator login blocked; do not silently choose local identity or create passwords.
- [ ] After rehearsal/review passes, back up actual lab database/files and apply safe migrations. Only bind Operator when issuer/subject is obtained through trusted provider configuration. Restart Clary-Lab only. Use existing valid localhost dev certificate for local API HTTPS and Vite proxy; verify actual secure cookie/CSRF login on localhost5174. No private-key export or OS trust modification needed by default.
- [ ] Read-only smoke existing saved assets/documents and permissions; no synthetic real inventory. Report working port5174 and precise remaining identity configuration only after verification.

## Sequencing and inherited checkpoint

Original foundation Tasks1–7 reviewed complete; Task8 has a stopped checkpoint with19/19 isolated tests/build passing but unreviewed and obsolete membership roles. Before Task1 execution, package and review that checkpoint to preserve directory/stable-ID integration; superseded role UI is replaced by this plan. Retain original progress ledger and snapshots. This plan owns a separate SDD workspace and starts from current source snapshot. No parallel implementers modifying shared files.

## Plan self-review

Spec coverage: sole owner/bootstrap Task1–2; companies/subscriptions/seat races/invitations Task3; backend roles/revocation Task2–4; presence/audit Task4; private management page/company settings Task5; approved login Task6; retained inventory work Task7; preservation and deployment Task8. All five Review Focus conditions have named owning tests. No username/password supplied in chat is persisted or used. Production provider/verified identity is an explicit external configuration dependency; it does not block implementation or isolated tests.
