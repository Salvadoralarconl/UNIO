# Ubuntu hosting at unio.getunio.dev

This is a hosting change to the existing app. The AI behavior, login page and company Administrator provisioning remain unchanged. Public signup is disabled. The current AI trial has unresolved conversational acceptance issues; hosting checks do not certify AI quality.

The existing **host-based Cloudflare Tunnel** reaches Nginx on `127.0.0.1:5180`. Nginx serves the production frontend and proxies `/api` over verified private HTTPS to the API. API and database ports are not published. No forwarded-header trust is enabled. The existing network-peer login throttle remains shared behind the proxy (30 anonymous POST requests/minute).

## First install: run these yourself in the Ubuntu SSH shell

Start in your existing UNIO clone. Do not use the development `compose.yaml` for this deployment. These commands use an independent `unio` Compose project and do not change Folio or Ollama services.

Prerequisites: Docker Engine with the Compose plugin, Git, Bash, OpenSSL and curl; permission to run Docker; sufficient disk for images, database/uploads and paired backups; and the existing host-based tunnel. [Local verification results](hosting-verification-2026-10-11.md) describe what has and has not been checked.

```bash
git pull --ff-only
# Confirm 5180 is free. If occupied, select another free loopback port by adding
# UNIO_HTTP_PORT=<port> to your private .env.production, and use that tunnel port.
ss -ltn '( sport = :5180 )'
bash deploy/prepare-env.sh
```

The script asks for the existing **private Ollama origin reachable from containers**, then creates a random database password privately. Use the known trusted server address; do not use `localhost` to mean the host from inside a container. It does not print the password. Do not publish `.env.production`. No OpenAI key is required.

```bash
bash deploy/compose.sh config --quiet
bash deploy/compose.sh build
bash deploy/compose.sh run --rm tls-init
bash deploy/compose.sh up -d database
# Run ONLY on this new empty database:
bash deploy/compose.sh run --rm api --fresh-bootstrap
bash deploy/compose.sh up -d api web
bash deploy/compose.sh ps
curl --fail -H 'Host: unio.getunio.dev' http://127.0.0.1:5180/healthz
curl --fail -H 'Host: unio.getunio.dev' http://127.0.0.1:5180/api/auth/config
```

All three services must become healthy; auth config should report Local with `allowsRegistration: false`. `/api/health` reports only AI configuration. The API container healthcheck separately verifies database migration readiness; it does not call Ollama. An empty database has no private users or imported demo content.

Nginx refreshes Docker DNS when the API address changes. API recreation still causes a brief outage while the service starts and DNS retries; the static frontend health check alone does not certify API readiness.

## Existing Cloudflare Tunnel

In your existing tunnel's hostname routing, add **only** `unio.getunio.dev` -> `http://127.0.0.1:5180` (or your chosen free loopback port). Preserve its other routes. Keep the browser-facing hostname HTTPS. The origin Host header must be `unio.getunio.dev`. The web server redirects requests which Cloudflare identifies as HTTP to HTTPS.

No public router/firewall port is required, and never route the tunnel to PostgreSQL or Ollama. This configuration assumes cloudflared runs on the host. A containerized tunnel needs a separately reviewed private-network connection; its `localhost` does not mean Ubuntu.

The private API certificate is created locally inside a Docker volume and validated by Nginx. No OS/browser certificate trust change and no `noTLSVerify` setting is needed. The certificate lasts one year. Retain the TLS volume; plan an offline renewal/recreation of the private pair before expiry, followed by API/web recreation. The initializer preserves an existing valid pair and refuses an incomplete pair.

## First Operator setup: private link, not a shared password

After the hostname is reachable over HTTPS, run:

```bash
bash deploy/compose.sh run --rm --no-deps \
  -e BRIE_OPERATOR_OUTPUT_FILE=/app/private/Brie/operator-setup.txt \
  api --initialize-local-operator
umask 077
mkdir -p "$HOME/.local/share/unio"
bash deploy/compose.sh run --rm --no-deps -T --entrypoint cat api \
  /app/private/Brie/operator-setup.txt > "$HOME/.local/share/unio/operator-setup.txt"
chmod 600 "$HOME/.local/share/unio/operator-setup.txt"
```

Open that file privately (or copy it privately to your own computer) and follow its 30-minute, single-use HTTPS setup link. **Do not paste it into chat, GitHub or a shared log.** Choose your password yourself. Initialization refuses an already bound Operator. An expired link needs the explicit offline `--recover-local-operator` operation with a *new* private output filename, not a second owner.

Sign in freshly. Create a company and its initial Administrator, then invite/create the test account using the existing UI. Company Administrators retain their existing scoped account-management powers. No public signup or default/shared password is added.

## Required acceptance before sharing with Mike

- Confirm HTTPS login, session reload and logout; an anonymous protected API request must return 401.
- Check your Operator and company Administrator permissions, and a separate test user limited to its intended company. Confirm the user cannot read another company's IDs.
- Verify a session remains valid after recreating the API container; key storage is persistent.
- Verify the server can reach the private Ollama endpoint and that the existing generation and embedding models are installed. A real synthetic AI request and an approved synthetic document indexing check are still needed to confirm AI wiring. Do not infer this from `/api/health`.
- Share only the public site link and privately created test credentials. GitHub publication does not migrate data or deploy the site.

## Demo content is not a database migration

Existing demo documents in the repository are exports/source files. They do not automatically restore folders, relationships, conversations, inventory, approval/index state, company memberships or credentials. `scripts/ItTrainingImport` contains old hard-coded company/user IDs: **do not run it against this new database**. Use a separately prepared safe seed/import or manually create the approved demo records; do not import a raw live database or copy account credentials.

## Persistence, backup and upgrades

Named volumes preserve database, uploads, Data Protection keys, private bootstrap files and origin TLS material. Never use `down -v` on your real deployment. Data Protection uses the stable app name `UNIO`; keep its private volume across restarts and releases. Key files and backups require private host access.

```bash
# Pauses writes, captures database and matching files/config, resumes prior services:
bash deploy/backup.sh
```

Backups default to `$HOME/.local/share/unio/backups`, outside Git, and contain secrets. Keep a protected off-host copy through your own backup process. Test a restore into a separate disposable Compose project before relying on a backup: restore the custom-format dump with `pg_restore` to an empty database, restore matching uploads/keys/TLS/private files, then verify login and records. Never rehearse against the live volumes.

For an upgrade, back up first, preserve the prior commit/image tags, pull with `--ff-only`, set `UNIO_RELEASE` in private configuration to the new commit, build, pause app writes, run `api --migrate`, then recreate API/web. Never run `--fresh-bootstrap` on an existing database. If the schema changed, rollback requires the paired pre-upgrade database and file backup plus the previous image/config; rolling back only the image is insufficient.
