# Clary Lab operations

Only operate in `C:/Projects/Apps/Clary-Lab`. SourceDesk API5080/database5432 are separate. Lab database is the existing `clary-lab-database-1` on127.0.0.1:5433; the old Lab API `clary-lab-api-1` occupies5081. No script stops either container automatically.

## Stage1 checkpoint and activation gates

The live Lab is still on its original schema/API. Local-login activation follows the reviewed Task4 coordinated rollout with a fresh paired backup. The user has approved the concrete legacy-link archive/clear repair and matching frontend/backend deployment; independent local-login security review must pass first. Google activation is cancelled. A missing database, failed rehearsal or missing credentials is a blocker, never a passing/skipped check.

The actual legacy database contains one dangling document `replaced_by` pointer. Migration preflight fails before writing schema or rewriting links. The reviewed proposal in `scripts/repair-proposals/001-archive-dangling-document-replacement.sql` saves the exact original pair in `legacy_document_replacement_archive`, then clears only that invalid pointer. It refuses changed conflict counts/IDs. The user has approved this exact actual repair as part of the reviewed Task4 rollout; never broaden the target. Keep the archive permanently with backups. Reversing to the original legacy state requires the original database backup because the migrated foreign key deliberately rejects the dangling pointer.

## Private Local identity configuration

Normal startup defaults to Local and requires no Google client or secret. Save private database configuration in root `.env`; authentication settings may be explicit:

```dotenv
Identity__Mode=Local
Identity__FrontendOrigin=http://localhost:5174
```

The script imports only allowlisted literal process values and forces development-provider identity off in Local mode. API transport remains `https://localhost:5081`, frontend `http://localhost:5174`; no caller Host or return URL selects either. Explicit `Identity__Mode=Oidc` retains historical configured OIDC startup for separate compatibility checks, but is not the Lab activation path.

## Explicit startup operations

Run from the project root:

```powershell
./scripts/Start-Clary.ps1 -Mode Normal -Provider Ollama
./scripts/Start-Clary.ps1 -Mode Migrate -Provider Ollama
./scripts/Start-Clary.ps1 -Mode FreshBootstrap -Provider Ollama
```

Normal startup serves HTTPSlocalhost5081 using the existing valid Windows localhost development certificate and never runs schema SQL. Migrate is offline, applies pending numbered migrations002–008 and exits without authentication/listener setup. FreshBootstrap is offline, rejects any existing application schema, applies001 once and then numbered migrations; it cannot be repeated on existing data. Legacy `--initialize` is disabled, including Docker ENTRYPOINT. Do not run fresh bootstrap against actual Lab data. Supplying `ConnectionStrings__Knowledge` through the private process environment selects the database; otherwise local scripts use Lab5433 and the private `.env` password.

Existing certificate check: `C:/Users/sapro/.dotnet/dotnet.exe dev-certs https --check`. No key export, new certificate or OS trust change is part of this workflow. A restricted process can falsely report no certificate; the user process can read the existing certificate. Start any background helper with `Start-Process -WindowStyle Hidden`; keep private inherited environment off command lines and protect redirected diagnostics outside source.

The supported local transport uses a Windows API process and Docker database. `-Compose` refuses to pretend that the HTTP container supports Local HTTPS. After the activation gates pass, stop **only** exact `clary-lab-api-1` to release5081; retain its container as rollback material. Preserve both database containers and SourceDesk. The new image's default entrypoint never initializes/migrates. External production TLS/container deployment needs its own explicit TLS/provider configuration.

Frontend staging retains the old fixed HTTP API proxy by default. After actual HTTPS API activation, start a new frontend process: `cd frontend`, set `$env:CLARY_LOCAL_HTTPS='1'`, then `npm run dev`, and browse `http://localhost:5174`. Vite binds localhost and proxies only `/api` to fixed `https://localhost:5081` when that explicit process flag is set. Certificate verification is relaxed only for this fixed loopback development proxy; production builds contain no proxy. Removing the flag restores the fixed `http://127.0.0.1:5081` proxy for old-API rollback. Use localhost for the browser's Secure cookie behavior. Do not advertise working authentication based on the build or a cookie probe: verify the actual privately completed username/password setup, login, session, antiforgery-protected request and logout after rollout. Never put passwords or credential tokens on command lines or logs.

## Operator and company provisioning

Until `operator_owner` is bound, Operator operations fail closed. Task4 uses the reviewed offline Local bootstrap to bind a random immutable internal identity and writes its short-lived setup link to a private file. The user completes the five-field form and chooses a password privately. Never reuse a password posted in chat, promote a first login, or bind authority to email/username. Existing nonlocal owner recovery needs a separately reviewed offline migration. No public registration or bootstrap route exists.

After the review, paired backup and actual migrations, the offline command is `./scripts/Initialize-LocalOperator.ps1 -OutputFile "$env:LOCALAPPDATA/Brie/operator-setup.txt"`, with the private actual Lab connection inherited through `ConnectionStrings__Knowledge`. Choose a new output filename if one exists; never overwrite or read the raw link into logs/chat. The bootstrap creates a protected user-only file, refuses an existing owner, and issues a single-use 30-minute setup link without selecting a password. Open that private file locally and complete setup, then sign in afresh. Until the user does so, report ready-for-setup rather than verified authenticated access.

The Operator creates a company and initial Administrator atomically with first/last names, optional email and plan. Enabled accounts consume a seat before password setup. Disabled accounts issue no link until enabled and explicitly provisioned. Copy each once-issued 30-minute credential link for private manual delivery. Existing Legacy content retains IDs and needs explicitly authorized membership.

If the owner account is disabled, ordinary management cannot replace the singleton owner. Recover through a trusted offline administrator after backing up: diagnose the existing owner/user row, restore only that same identity's active status as an explicitly reviewed recovery, then authenticate again. Never replace issuer/subject, drop ownership guards or promote the first login. Company suspension/disabled accounts lose access on the next protected request; reactivation does not silently enable accounts. Session revocation and presence are separate: online means authenticated unexpired activity within five minutes, counted by distinct user.

## Invitations and recovery

Administrator may create/invite only Editors/Viewers; Operator alone assigns Administrator and plan. Enabled accounts plus pending unexpired invitations consume seats. Invitations expire after seven days. Copy the one-time shown link for manual delivery; no email is sent automatically. Never log its token. An anonymous recipient opens `/accept-invitation#token=...`, enters first name, last name, username, password and confirmation; completion converts the reserved seat exactly once and requires a fresh username/password login. Setup links use `/setup-account#token=...`; both fragments are captured and stripped before network effects and kept only in memory. Reset keeps identity/profile fields read-only. Contact the administrator for recovery; there is no email reset delivery. Revoking/reissuing invalidates the prior reservation/token. A Viewer cannot mutate; an Editor has no deletion capability by default. Denial/revocation clears frontend company data.

## Preservation rehearsal, backup and rollback

```powershell
./scripts/Rehearse-Preservation.ps1
# Explicitly authorized dryrun of the concrete proposal, isolated copy only:
./scripts/Rehearse-Preservation.ps1 -RehearseDanglingReplacementRepair
```

The runner pg_dumps actual `clary` read-only, copies `/app/uploads` from exact Lab API into `~/Clary-Lab-Backups/<timestamp>`, restores a generated `clary_rehearsal_*` database, migrates only that copy and compares sorted full-row original-column hashes/counts. Original prose, IDs, passage vectors and relationships are compared without printing contents. It requires migration008 and confirms no legacy credentials or setup tokens were inferred. It checks copied file SHA256 and migrated allowlisted paths, records missing existing files, saves the migrated copy, restores the original dump into the copy and checks exact original rows again. Synthetic role-mapping/multi-company-conflict checks run only in unique test schemas. It drops only its generated database, never actual Lab. Backups and row/file manifests remain outside source/public directories and must stay private.

For actual rollout, pause Lab writes before the final paired database/files backup to obtain a consistent rollback point. Recheck row counts/conflicts and backup readability. Apply only the user-approved pre-repair and numbered migrations. Before local HTTPS startup, copy the complete backed-up uploads directory into the local API content root `backend/Clary.Api/uploads` and compare every file SHA256; preserve all presently unreferenced files too. Do not leave the uploads only in the stopped container. Then start HTTPS Lab API. Read saved assets/documents and permissions without synthetic live uploads or inventory. If activation fails, stop the new Lab process, restore the full pre-rollout Lab database and uploads together, and restart the retained old Lab API. Restore the original backup into a temporary database first to verify it; never copy migrated rows piecemeal into the original schema. SourceDesk remains untouched throughout.

### Offline local Operator recovery

If the initial setup link expires or the local Operator forgets their password, run `scripts/Recover-LocalOperator.ps1 -OutputFile "$env:LOCALAPPDATA/Brie/recovery-unique.txt"` with `ConnectionStrings__Knowledge` set privately in the process environment. The distinct API command is `--recover-local-operator` and accepts no other arguments; `BRIE_OPERATOR_OUTPUT_FILE` selects the new private file. No password is accepted by the command or script.

Recovery retains the same active companyless local singleton identity and refuses absent, nonlocal, mismatched or disabled ownership. It issues Setup when the password is unset and Reset otherwise. Prior credential links and sessions are revoked in the same audited transaction; the new fragment link expires after 30 minutes and is written only to a new user-private file beneath the current user's local Brie directory. Open that file privately and delete it after use. Never paste it into logs or chat. Recovery does not migrate or reassign a nonlocal owner.

Every signed-in Local user can use **Change password**, including the companyless Operator. Enter current password, new password and confirmation; saving requires fresh sign-in and clears the current company session.
