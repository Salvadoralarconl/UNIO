# Brie Local Login Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (already selected by user). Steps use checkbox syntax for tracking.

**Goal:** Provide private Brie username/password login and first/last-name account setup, preserving reviewed company management and deploying the verified result on localhost5174.

**Architecture:** Add local credentials and single-use credential tokens to existing PostgreSQL user IDs and secure persisted-cookie sessions. Reuse account-service company locks and authority checks, rather than create a parallel membership/seat system. Local mode replaces Google activation; there is no public signup or first-user promotion.

**Tech Stack:** .NET10, ASP.NET Core Identity PasswordHasher, Npgsql/PostgreSQL, React/TypeScript/Vite, existing Playwright isolated fixtures and xUnit integration fixtures.

**Spec:** `docs/superpowers/specs/2026-10-08-local-login-design.md` (approved).

## Global Constraints

- SourceDesk remains untouched. Actual Lab stays unchanged until reviewed activation with a fresh paired backup.
- Append migration008 (or next unused number); never change002–007. Preserve existing user/document/asset/directory IDs, prose and files.
- No public company registration, role selection, Administrator promotion or Operator registration.
- The Operator singleton remains bound to immutable internal identity, never username or email. No reuse of the password posted in chat.
- Usernames are trimmed, case-insensitive, globally unique,3–64 ASCII letters/digits/dot/underscore/hyphen.
- First and last names are required for new local setup, each1–100 characters, allowing Unicode. Do not infer or overwrite legacy names.
- Passwords are15–128 characters, allow spaces/Unicode, are never trimmed/truncated, and confirmation must match.
- Use Identity V3 PBKDF2-HMAC-SHA512 with at least220,000 iterations and versioned rehash-on-login. Never log or return password/hash/raw-token data except the explicitly once-issued link.
- Credential setup/reset tokens are random, hash-only, purpose/target-bound, single-use and expire after30 minutes. Invitation reservations remain seven days.
- Persist five consecutive failures then15-minute account lockout; bound IP request rates without trusting arbitrary forwarded headers. Generic anonymous errors and dummy verification prevent obvious username enumeration.
- Enabled accounts consume a seat before password setup; a credential token adds no seat. Invitation acceptance converts its reserved seat exactly once.
- Keep secure HttpOnly cookies, CSRF, eight-hour fixed sessions, heartbeat, permanent revocation, company isolation and transactional authority checks.
- Preserve the approved warm-gradient split panel, white forms, black primary actions, custom dropdowns and aligned controls.
- Strict browser fixtures block unmocked APIs. No synthetic actual inventory/uploads, no Git initialization, no worker subagents, no Google credential request/activation.
- Existing subagent-driven execution method persists. Snapshot-based reports/reviews replace Git commits because this isolated copy has no Git metadata.

## Review Focus

- A duplicate username race must produce one credential owner and roll back the losing membership/seat change (Tasks1–2).
- A reset/setup token issued before disable or another reset must not resurrect credentials or old sessions (Task2).
- A lock-only wait must not let an invitation, setup token or session pass its expiry boundary (Task2).
- A public setup payload containing role/company/owner identity must not influence those server-chosen values (Task2).
- A slow login/setup/reset response after logout/company change must not repopulate company state or expose a once-issued link to the next company (Task3).

## File and interface map

Add `backend/Clary.Api/LocalIdentity/` with narrow contracts, credential repository, password service, token service, bootstrap and HTTP endpoints. Existing `IdentityEndpoints.cs` retains shared cookie lifecycle/authority middleware; register local public endpoints explicitly, never via a session-prefix exception. `Accounts/CompanyAccountRepository.cs` remains authority/seat/audit owner for account mutations and receives transaction-scoped local provisioning hooks, not browser identity claims.

Define these shared contracts in `LocalIdentity/LocalIdentityContracts.cs`:

```csharp
public enum CredentialTokenPurpose { Setup, Reset }
public record LocalProfile(string? Username,string? FirstName,string? LastName,bool PasswordSet);
public record LoginInput(string Username,string Password);
public record CredentialSetupInput(string Token,string Username,string FirstName,string LastName,string Password,string ConfirmPassword);
public record PasswordChangeInput(string CurrentPassword,string Password,string ConfirmPassword);
public record CredentialLink(Guid UserId,DateTimeOffset ExpiresAt,string Link);
public record LocalAccountInput(string FirstName,string LastName,string? Email,CompanyRole Role,bool Enabled,int? Revision);
public record LocalCompanyCreateInput(string Name,SubscriptionInput Subscription,LocalAccountInput Administrator);
public record ProvisionedAccount(AccountDto Account,CredentialLink Setup);
public record ProvisionedCompany(CompanyAdminDto Company,ProvisionedAccount Administrator);
```

Use fixed internal issuer `https://brie.local/identity` for newly generated local users and a random UUID subject. The issuer is an internal namespace, never a network destination or browser input. Existing user identities remain unchanged; authorized local credential provisioning can attach to an existing permitted ordinary user ID while retaining its issuer/subject. Never attach the protected owner through a company manager route. Existing nonlocal owner recovery requires a separately explicit migration; actual Lab currently has no owner.

`LocalPasswordService.Hash(string password) -> string`, `Verify(string hash,string password) -> PasswordVerificationResult`, `Validate(string password,string confirmation) -> bool` wrap the framework hasher and exact limits. `LocalCredentialTokens.IssueAsync(NpgsqlConnection,NpgsqlTransaction,Guid userId,CredentialTokenPurpose purpose,CancellationToken) -> CredentialLink` stores no raw token and runs in the caller transaction.

The local mode session response adds `authenticationMode:"Local"` and optional local profile fields. Anonymous `GET /api/auth/config` returns only `{mode:"Local",allowsRegistration:false}`. Explicit alternative mode `Oidc` may preserve historical authentication tests, but normal startup defaults explicitly to Local and requires no OIDC settings.

## Task1: Local credentials, tokens and offline Operator bootstrap

**Files:** Create `database/migrations/008-local-credentials.sql`, `LocalIdentity/LocalIdentityContracts.cs`, `LocalPasswordService.cs`, `LocalCredentialRepository.cs`, `LocalCredentialTokens.cs`, `LocalOperatorBootstrap.cs`, `scripts/Initialize-LocalOperator.ps1`; modify `Program.cs` offline branch; tests `LocalCredentialTests.cs`, `LocalOperatorBootstrapTests.cs`.

**Interfaces:** Produce the shared contracts above and `LocalOperatorBootstrap.InitializeAsync(string outputFile,CancellationToken) -> Task<Guid>`. The command creates one companyless owner identity, an unset-password credential and one30-minute setup link in a user-private file. It never accepts a password argument or prints the link. An existing different/nonlocal owner is refused; rerunning cannot create a second owner. Do not grant a membership.

- [ ] Write tests `UsernameNormalizationAndUniqueConstraint`: exact3/64 limits, invalid punctuation, Unicode rejection for username only, nullable pre-setup username, competing normalized names yield one winner; preserve other transaction state.
- [ ] Write `PasswordHashAndBounds`: fresh salts differ, correct/wrong results, malformed hash fails safely,15/128 boundaries, spaces preserved, no trimming, iteration metadata meets220000 and framework rehash result is honored.
- [ ] Write `TokenPurposeExpiryAndSecrecy` and `OnlyOneOfflineOwner`: hash-only token storage, expiry+30min, purpose mismatch, fresh owner no membership, concurrent bootstrap exactly one owner, existing immutable owner unchanged, no password/link console output.
- [ ] Run the named tests against isolated schemas and confirm intended RED before implementation.
- [ ] Implement008 with credential row keyed to user ID, normalized unique username, hash nullable before setup, credential revision, counters/lockout; token hash32bytes/purpose/target/expiry/consumed/revoked; first/last profile values without guessed legacy migration. Add a bounded local-auth security-event table for anonymous failures/lockout with nullable actor rather than falsely attributing an attacker to a user.
- [ ] Implement primitives/bootstrap with the existing singleton table lock and account audit for provisioning, without weakening its immutability triggers.
- [ ] Run focused tests and build. Append exact schema/contracts/commands/results to this plan's task-1-report; independent review gate before Task2.

## Task2: Login, private provisioning, invitation setup and recovery

**Files:** Create `LocalIdentity/LocalAuthenticationService.cs`, `LocalProvisioningService.cs`, `LocalIdentityEndpoints.cs`; modify `Identity/IdentityEndpoints.cs`, `Accounts/AccountEndpoints.cs`, `CompanyAccountRepository.cs`, `AccountContracts.cs`, `InvitationService.cs` only as needed; tests `LocalLoginTests.cs`, `LocalProvisioningTests.cs`, `LocalRecoveryTests.cs`.

**Interfaces:** Register explicit local routes:

- Anonymous `GET /api/auth/config`, `GET /api/auth/antiforgery` and CSRF-protected `POST /api/auth/login` with LoginInput. Success creates the existing persisted cookie session and returns `{requiresSessionReload:true}`; invalid/disabled/locked/unconfigured credentials return generic401, IP throttle429.
- Anonymous CSRF-protected `POST /api/auth/setup` with CredentialSetupInput consumes an existing credential token; `POST /api/auth/invitations/complete` with the same shape consumes an invitation. Success returns `{freshLoginRequired:true}` and no authenticated session or role/company authority.
- Anonymous CSRF-protected `POST /api/auth/setup/details` with `{token}` returns only valid-token `{purpose,username,firstName,lastName,expiresAt}` for the setup form, does not consume the token and never exposes role/company/password/hash. Throttle it and use generic invalid-link errors. Reset retains the existing normalized username and profile; its form shows those values read-only and changes only the password. Setup may choose the initial username/profile.
- Authenticated CSRF-protected `POST /api/auth/password` with PasswordChangeInput changes own password only, revokes sessions/tokens and clears cookie, returning `{freshLoginRequired:true}`.
- Protected `POST /api/operator/companies/{company}/accounts/{user}/credential-link` and own-company equivalent issue Setup/Reset according to whether a password exists; expected account revision is required. Operator may target company Administrator; company Administrator only other Editor/Viewer. Owner reset is offline-only.
- In Local mode existing company/account creation routes accept LocalCompanyCreateInput/LocalAccountInput, never issuer/subject, and return ProvisionedCompany/ProvisionedAccount with a once-issued setup link. Local account edits accept profile/role/status/revision without password fields. Existing-ID Administrator selection retains userId/accountRevision/companyRevision contract; new Administrator provisioning gets the same setup hook. Add optional LocalProfile to AccountDto without breaking historical records.

- [ ] Write actual shared-handler RED tests for successful login/session/logout, generic unknown/wrong/disabled response, anonymous CSRF, fifth-failure15-minute lockout and bounded IP throttling. Verify no attacker-supplied iss/sub/role/company enters a principal; password verification derives identity from the persisted credential/user.
- [ ] Write private provisioning negatives for foreign company/self/owner/Administrator target, atomic company+Admin+credential+token rollback and no extra setup-token seat. Verify existing user IDs survive authorized credential attachment and legacy profiles remain unchanged until set.
- [ ] Write anonymous invitation setup tests: role/company injection ignored or400, single-use token, duplicate username race, capacity reservation conversion including reduced over-limit plan, wrong token purpose and both user/session lock-only expiry cases. No double seat and no partial account on audit failure.
- [ ] Write recovery tests: token invalidated by reset/password change/disable, stale revision, expiry after locks, old cookies remain revoked after account/company reactivation, companyless owner works without company access, manager cannot reset protected owner or self by override.
- [ ] Confirm intended RED, then implement Local mode registration. Refactor transaction-scoped cores in existing account service so local credentials/token append inside the SAME authorized company-first transaction; do not nest independently committed provisioning transactions or duplicate seat policy.
- [ ] Lock company (when applicable), then user, credential and session/token consistently; use fresh database time after lock acquisition. Login may read a candidate hash first, but must recheck credential version/status under the ordered transaction before issuing a session. Rate-limit before expensive hash work; never hold shared authority then upgrade in conflicting order.
- [ ] Implement password-free audit DTOs and bounded security events; current password change is distinct from manager reset. Keep all public paths explicit and mutation CSRF enforced; existing protected paths remain fail-closed.
- [ ] Run focused tests plus affected identity/account/invitation tests. Write task-2-report with exact status/error/payload contracts for Task3; independent security review gate.

## Task3: Local login, account setup forms and management integration

**Files:** Modify `frontend/src/LoginShell.tsx`, `login.css`, `InvitationAccept.tsx`, `CompanySession.tsx`, `companyApi.ts`, `accountApi.ts`, `CompanyAccountSettings.tsx`, `OperatorDashboard.tsx`; create `CredentialSetup.tsx`, `authenticationApi.ts`; update strict fixtures; tests `local-login.spec.ts`, `local-setup.spec.ts`, `local-account-management.spec.ts`. Modify `scripts/Clary-Startup.ps1`, `Start-Clary.ps1`, startup tests and operations docs for explicit Local mode.

**Interfaces:** Use Task2 endpoints. `/setup-account#token=...` captures credential token and `/accept-invitation#token=...` captures invitation token before network effects, strips fragment and keeps it only in memory. Login fields are username/password; setup fields first name,last name,username,password,confirmation. Enter submits once. No Google client settings required or requested.

- [ ] Write strict isolated RED tests for login form, all five setup fields, username/password validation and once-submit, expired/replayed links, invitation conversion and explicit fresh login.
- [ ] Write Operator company/Admin setup and own-Admin Editor/Viewer creation tests using human profile fields, once-copyable credential links, protected reset targets and enabled/pending distinction. Preserve independent search/mutation request ownership and stable IDs/revisions.
- [ ] Write delayed login/setup/reset response after logout/company change tests and visible heartbeat/revocation tests. Auth requests use explicit anonymous helper/antiforgery semantics rather than a general bypass of company-scoped requests.
- [ ] Implement forms using existing approved split panel, aligned heights, white controls and black primary button; desktop/mobile no horizontal overflow. Include contact-administrator guidance for forgotten password; do not pretend email reset delivery exists.
- [ ] Update startup allowlist/mode: `Identity__Mode=Local` requires no Google client/secret. Keep HTTPSlocalhost5081 and fixed localhost5174 return configuration, existing certificate and explicit frontend HTTPS opt-in. No password or token on command lines/logs.
- [ ] Run build and focused tests; inspect desktop/mobile login/setup/manager screenshots. Write task-3-report; independent review gate.

## Task4: Preservation, security integration review and actual local rollout

**Files:** Update `scripts/Rehearse-Preservation.ps1`, `docs/company-foundation-operations.md`, `LAB-README.md`, local bootstrap/startup docs/tests as necessary. No unrelated product features.

- [ ] Confirm predecessor consolidated whole-change F1–F3 fixes passed scoped rereview. Keep the corrected exact repair proposal and user approval; do not ask again for approved archive-clear, Lab migration/restart or subagent method.
- [ ] Rehearse migration008 on an isolated actual-data copy with approved exact archive-clear repair; compare existing IDs/content/relationships/files and rollback to the paired original snapshot. Include nonempty isolated fixtures for role/seat/credential conflicts because actual legacy memberships are empty.
- [ ] Run full backend, frontend build, full strict isolated browser suite and startup/config checks once on final integration; failures/new edits justify focused retests. Existing live tests remain excluded.
- [ ] Most-capable final security/integration review includes local credential bootstrap/public routes/anonymous CSRF/lockout/password/token recovery plus the retained company/seat/session guarantees. One consolidated correction wave and scoped rereview; preserve report and rulings.
- [ ] After review, pause only Lab writes and take fresh paired database/files backup outside source/public roots. Recheck exact repair precondition. Apply approved repair and immutable migrations only to Lab, preserve and copy uploads into the local API root, retain old Lab container for rollback. Never stop SourceDesk or either database container.
- [ ] Start reviewed HTTPS Lab API and frontend5174 with the explicit fixed local HTTPS opt-in. Offline bootstrap the sole local Operator to a user-private setup-link file. Do not choose or print a password; user completes the real setup form privately.
- [ ] Verify actual setup/login secure cookie/CSRF, logout/revocation and private Operator-only route after user completes credentials. Read-only smoke existing saved records/documents; no synthetic actual inventory/uploads. If user setup is pending, report its exact link/file location and distinguish ready-for-setup from verified authenticated access.
- [ ] Report verified access URL and limitations only from observed results. Retain no-Git snapshots/ledgers and all rulings. Billing and deferred Task7 inventory-history improvements remain separate follow-ups.

## Plan self-review

Coverage: credential/profile rules and owner bootstrap Task1; login/CSRF/rate limits/company seats/invites/reset/audit Task2; forms/names/style/session races/startup Task3; preservation/security/actual rollout Task4. The five Review Focus cases map to named Task1–3 tests. Types and HTTP contracts are shared above; implementations report any exact refinements before consumers start. Existing isolated-copy/no-Git and chosen subagent execution rulings persist. No Google activation, public signup, payment collection or original Task7 work is implied.
