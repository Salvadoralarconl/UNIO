# Hosting verification — 2026-10-11

Base: `3b4de0e658c28a2374971d7f4ee418165e88b711` on `Salvadoralarconl/UNIO` main. Checks ran in an isolated checkout and disposable local Docker databases/volumes. The existing working app, database, Folio, Ubuntu server and Cloudflare configuration were not changed. No live model calls were made; the test AI origin was an unreachable loopback port.

## Passing checks

- .NET API and production frontend builds; both deployment images built. Frontend retains the existing bundle-size warning.
- 129 selected integration tests passed, zero failed: hosted bootstrap origins and key persistence; local login, provisioning, Operator bootstrap/recovery and session retention; company session/account/invitation permissions; company knowledge and conversation isolation. PostgreSQL vector was installed once in the disposable test database's public schema before parallel fixtures ran.
- After examining the baseline failures, all 13 existing `AssetConfigurationTests` passed against the hosting commit. They cover normal company creation seeding and transaction rollback, configuration/foreign-ID permissions, and migration backfilling assets/kits without rewriting request snapshots. This adds relevant evidence but does not replace the two inventory-isolation tests that stop early.
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
| `CompanyInventoryIsolationTests.OtherCompanyIdsAreInvisible` | Test inserts a second company directly without its asset types; asset creation fails before the cross-company assertions. Production company creation calls `AssetConfigurationRepository.SeedDefaultsAsync`. This failure is not evidence of a data leak, but this test does not prove inventory isolation. |
| `AssetConfigurationValueTests.CustomTypeAndValuesAreReturned` | The custom asset/type/value response assertions pass, then the test expects zero inventory-documentation records and receives one. The current asset creation path automatically provisions documentation. Its later protected-property assertion is not reached. |
| `CompanyMigrationTests.LegacyDataKeepsIdsAndRelations` | Snapshot excludes older added columns but not `asset_type_id` and `custom_fields` added by migration 010. The observed original fields match, with those two fields added. Later assertions are not reached; this is not full legacy-data migration acceptance. |
| `CompanyMigrationTests.CompanyUniquenessAndChildReferencesAreScoped` | Test creates a company using raw SQL after migrations, omits asset-type seeding, then inserts an asset whose default category lacks its company-scoped reference. PostgreSQL rejects it with 23503 before intended child-reference assertions. This demonstrates enforcement of that reference, not proof that every later assertion would pass. |

These findings do not block building and bootstrapping the tested empty trial database. They do block claiming a green full suite or complete legacy migration/inventory-isolation acceptance. Do not migrate existing company data on the strength of these hosting checks. Raw SQL/imports that bypass company provisioning can create incomplete companies; use the normal Operator company-creation flow. Before external users exercise inventory, verify separate-company asset list/get/update/activity and documentation access using normally provisioned companies. No unrelated feature or test fixes were included.

## Still required on Ubuntu

Follow [the hosting runbook](ubuntu-hosting.md), verify private Ollama reachability and installed generation/embedding models, then perform approved synthetic conversation/document tests. Verify public HTTPS, login, company permissions, session continuity and a complete restore rehearsal before sharing with another user. The existing conversational/MFA acceptance issues remain outside this hosting change. This is a trial deployment path, not a production-readiness certification.

Repository demo documents are not a database migration. The old hard-coded-ID import script must not be run against the fresh database. Credentials, private setup files, backups and test artifacts are excluded from the hosting commit.
