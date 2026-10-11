# Brie username and password login

Date: 2026-10-08. Proposed replacement for Google activation in the approved company-management work. Existing company, role, seat, audit, session and preservation requirements remain binding.

## Intent and scope

Users sign in directly to Brie with a username and password. Account setup collects first name, last name, username, password and password confirmation. Preserve the approved light split-panel design with warm gradient, white form and black primary action. Google configuration is no longer required. Do not reuse the password previously posted in chat.

Keep account creation private: the sole Operator creates companies and their Administrator; Administrators create or invite only Editors and Viewers. There is no public company registration, role selection, Administrator promotion or Operator registration. Username/password setup is not authority to create a company or choose membership.

## Recommended architecture

Add local credential/profile services to the current PostgreSQL user and secure-cookie session system. Replacing the entire identity stack would unnecessarily risk reviewed company isolation and session behavior. A hosted password provider would add another external configuration dependency. Use ASP.NET Core's maintained password hasher rather than custom cryptography.

Append migration008 or the next available number; never change002–007. Store a separate local credential row keyed by existing user ID, a globally unique normalized username, password hash, credential version, failed-attempt counters and lockout time. Add first/last-name profile fields without overwriting existing names or guessing names from old display strings. Existing records and memberships retain their IDs. New local accounts receive a server-generated immutable subject and fixed internal issuer; neither comes from browser input. The Operator singleton remains bound to that immutable internal identity, never username or email.

Select local authentication explicitly in startup configuration. Existing OIDC code may remain available only under its explicitly selected mode; do not show Google in local mode or require Google credentials. Existing secure HttpOnly cookies, antiforgery, persisted session IDs, fixed eight-hour expiry, transactional revalidation and permanent revocation remain in force.

## Operator setup

Provide an offline, one-time local Operator bootstrap. It serializes the existing singleton ownership guard and refuses a different or second owner. With no owner currently bound in the actual Lab, create a companyless internal identity and a short-lived setup token, not a default password. Deliver the setup link through a user-private local file, never console logs or command-line password arguments. The owner opens that link and chooses first name, last name, username and password. The earlier username may be offered as a suggestion; do not reuse the exposed password.

The public setup endpoint only consumes an already-issued token; it cannot bootstrap or promote an Operator. Token expiry/replay, disabled account and ownership mismatch fail closed. Resetting the sole Operator after forgotten credentials requires an explicit offline recovery command; no public first-user takeover or email-based owner reassignment.

## Company accounts and invitations

Replace issuer/subject inputs in local-mode management forms with human account fields. Creating a company remains atomic with its Administrator and subscription. A newly created enabled account consumes a seat even before password setup; its separate password-setup token does not reserve another seat. The end user sets their password through a one-time setup link. Administrators never view another user's password.

Pending invitations retain the seven-day seat reservation and existing server-chosen company/Editor-or-Viewer role. A local invitation link opens an anonymous account-setup form. Token possession is the invitation authorization; no fabricated Google email verification claim is created. Username and password do not choose role/company. Under the company lock, consume the current unexpired invitation, create the local identity/credential/membership, and convert the reserved seat exactly once. Recheck expiry after all relevant row-lock waits. Concurrent duplicate usernames or replay fail atomically; failures preserve the invitation and seat state. Valid reservation conversion after a limit reduction still succeeds without an extra seat.

Existing-account setup/reset tokens are distinct from invitation reservations and never create another membership or change roles. Reject attempts to use an invitation to attach an existing account to a different company or grant the owner a company membership. Existing company/owner protection remains applicable to account setup and recovery.

Use random high-entropy tokens, store only their hashes, bind purpose and target, and return links once. Credential setup/reset tokens expire after30 minutes and are single-use. Links use fragments, are stripped from the address bar and kept in memory, and are not logged or stored in browser storage. Sharing is manual in this phase; do not invent email delivery. Expired setup tokens can be reissued by the authorized account manager, invalidating predecessors without adding a seat.

## Password and profile rules

Usernames are trimmed, case-insensitive, globally unique,3–64 ASCII letters/digits/dot/underscore/hyphen. Preserve the chosen display spelling separately from normalization. First and last names are required for newly completed local setups, each1–100 characters; allow normal Unicode names. Existing legacy display names remain until a profile is explicitly completed.

Passwords are15–128 characters, support spaces and Unicode, and are never silently trimmed or truncated. Confirmation must match. Use ASP.NET Core Identity V3 hashing with PBKDF2-HMAC-SHA512 and at least220,000 iterations, consistent with the current OWASP recommendation; use versioned hashes and rehash on successful login when necessary. Passwords, hashes and raw tokens are excluded from logs, audit DTOs and API responses.

Anonymous login/setup endpoints require explicit antiforgery handling. Bound input sizes and apply login/setup/reset request throttles. Use a generic invalid-credentials response for unknown username, wrong password, disabled account and unavailable credentials; perform a dummy password verification for unknown users. Persist account lockout after five consecutive failures for15 minutes, and add bounded IP throttling without trusting arbitrary forwarded headers. Avoid token/username enumeration in anonymous recovery errors.

Password changes require the current password and revoke prior sessions. Authorized account managers may issue reset links only within their permitted target roles/company; company Administrators cannot reset themselves through a manager override or reset the Operator/Administrator. Provide the user's own current-password change separately. Password reset revokes old sessions and setup/reset tokens, changes no role/company/seat usage, and requires a fresh login. Disabled users and suspended companies cannot complete account setup or sign in for company access. Preserve companyless Operator recovery without implicit content access.

## Audit, interfaces and UI

Record local account provisioning, token issuance/consumption, password change/reset, lockout and recovery using token-free/password-free account audit entries in the same transaction as account changes. Do not put password data in before/after snapshots. Failed login security events must be bounded and must not leak whether a username exists.

Keep username/password sign-in, account setup and invitation setup as separate explicit forms. Keep first/last-name fields aligned with other controls. Show loading, validation, expired-link, retry and lockout guidance without exposing account existence. Enter submits once; duplicate submission is disabled. Heartbeat, logout, scope cancellation and stale-response guards continue to use the reviewed session layer.

Document exact endpoint/payload contracts in the implementation plan before coding. Public endpoints must be explicitly classified and may expose only login/antiforgery or single-use token consumption, never account-management authority. Company/operator creation, reset issuance and account changes remain backend-authorized and seat-locked.

## Verification and rollout

Test username normalization/races, password hashing and malformed hashes, generic failures/throttling/lockout, anonymous antiforgery, token purpose/expiry/replay/lock waits, company isolation, owner singleton protection and no public promotion. Verify disabled/suspended accounts and old sessions remain blocked, setup/reset never adds a second seat, invitations convert correctly at/over capacity, and secret-free transactional audit rollback. Use actual production handlers and isolated schemas.

Run strict mocked browser tests for login/setup/first-last-name validation, invitation conversion, manager creation/reset, keyboard submission, session loss and desktop/mobile layout. Review the authentication changes independently. Rehearse migration008 on the preserved copied database and retain original IDs/content/files. Resolve the existing whole-change review's three bounded findings separately.

The user has approved the rehearsed one-link archive-and-clear repair and Clary-Lab rollout after review. Before activation, pause Lab writes, take a fresh paired database/files backup, revalidate and apply the corrected exact repair, apply reviewed migrations, copy preserved uploads and start only the Lab API/frontend on reviewed localhost transport. SourceDesk remains untouched. Verify actual local setup/login, secure cookies, antiforgery, logout/revocation and read-only existing data before reporting5174 ready. No Google credentials or Google account binding remain required.

References: https://cheatsheetseries.owasp.org/cheatsheets/Password_Storage_Cheat_Sheet.html and https://learn.microsoft.com/en-us/aspnet/core/security/authentication/identity-configuration?view=aspnetcore-10.0 .
