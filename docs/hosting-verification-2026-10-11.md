# Hosting verification — 2026-10-11

Base: `3b4de0e658c28a2374971d7f4ee418165e88b711` on `Salvadoralarconl/UNIO` main. Checks ran in an isolated checkout and disposable local Docker databases/volumes. The existing working app, database, Folio, Ubuntu server and Cloudflare configuration were not changed. No live model calls were made; the test AI origin was an unreachable loopback port.

## Passing checks

- .NET API and production frontend builds; both deployment images built. Frontend retains the existing bundle-size warning.
- 129 selected integration tests passed, zero failed: hosted bootstrap origins and key persistence; local login, provisioning, Operator bootstrap/recovery and session retention; company session/account/invitation permissions; company knowledge and conversation isolation. PostgreSQL vector was installed once in the disposable test database's public schema before parallel fixtures ran.
- New database bootstrapped all 17 migrations. A second fresh bootstrap was refused. Idempotent migration application passed.
- All three production Compose services became healthy. Only the web loopback port was published; API/database had no host port bindings. Production Local identity reported public registration disabled.
- Real browser against the built production UI: HTTPS Operator setup, password login, authenticated session, Secure/HttpOnly cookie, rejection of logout without CSRF, valid-CSRF logout and anonymous session rejection. The authenticated session survived API recreation with persistent Data Protection keys. The local HTTPS test used a disposable certificate; production browser-facing TLS remains Cloudflare's responsibility.
- Nginx configuration validation and actual UI/API security-header checks. Private HTTPS upstream certificate verification remained enabled. API was deliberately recreated on different IPs on both Docker networks; Nginx recovered after DNS retry without web restart. Transient 502 responses occurred during replacement/retry, so this is not a zero-downtime deployment.
- Internal readiness was blocked through the web proxy. Readiness checks database migration count only and explicitly do not probe AI.
- Backup script paused/resumed only the disposable stack and produced a paired database/files/config backup. Its database dump restored successfully to a different disposable database with all 17 migration records. File archive extraction verified uploads, Data Protection keys, private storage and the TLS pair. A complete application login against restored volumes remains a deployment-owner restore rehearsal.
- Independent read-only review found two Nginx issues (DNS refresh and header inheritance); both were corrected and re-reviewed with no remaining critical/important findings.

## Existing failures on unchanged main

A separate checkout of the exact base ran 17 migration/inventory checks: 12 passed, five failed. These failures predate the hosting changes; no full-suite pass is claimed:

| Test | Observed baseline issue |
| --- | --- |
| `DatabaseStartupTests.FreshBootstrapCreatesCurrentSchemaAndCannotBeRepeated` | Expected 8 migrations; schema now has 17. |
| `CompanyInventoryIsolationTests.OtherCompanyIdsAreInvisible` | Asset-type validation exception. |
| `AssetConfigurationValueTests.CustomTypeAndValuesAreReturned` | Expected 0 values, received 1. |
| `CompanyMigrationTests.LegacyDataKeepsIdsAndRelations` | Expected serialized legacy record omits newer fields. |
| `CompanyMigrationTests.CompanyUniquenessAndChildReferencesAreScoped` | Inventory category foreign-key violation. |

## Still required on Ubuntu

Follow [the hosting runbook](ubuntu-hosting.md), verify private Ollama reachability and installed generation/embedding models, then perform approved synthetic conversation/document tests. Verify public HTTPS, login, company permissions, session continuity and a complete restore rehearsal before sharing with another user. The existing conversational/MFA acceptance issues remain outside this hosting change. This is a trial deployment path, not a production-readiness certification.

Repository demo documents are not a database migration. The old hard-coded-ID import script must not be run against the fresh database. Credentials, private setup files, backups and test artifacts are excluded from the hosting commit.
