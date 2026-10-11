# Brie local runbook

See the [evaluation history](brie-evaluation-log.md) for failed runs, fixes, exact synthetic transcripts and candidate acceptance gates. Staged candidate results do not imply deployment.

Verified 2026-10-11. This is the current operational reference for the existing application at `http://localhost:5174/`, with the authenticated API at `https://localhost:5081`. Older provider/evaluation notes in the README describe earlier checkpoints.

## Supported behavior

Brie retrieves approved, currently accessible company documentation before giving technical instructions. It displays one source action at a time, retains conditions and warnings, and provides clickable supporting citations. Follow-ups retain the current issue, supplied details and reported attempts; greetings do not fill pending action fields. Missing evidence returns exactly: “I couldn’t find a documented solution for this issue.” A user can explicitly request a support ticket.

Available actions depend on the signed-in user's existing permissions and current company:

| Action | Example / behavior |
| --- | --- |
| Create support ticket | `Create ticket Printer is offline`; asks for missing date, priority and assignee |
| Cancel standalone support ticket | `Cancel ticket <ticket UUID>`; previews the exact record and requires bound confirmation |
| Create inventory asset | `Create asset Reception laptop`; asks for missing tag, existing asset type and status |
| Assign / reassign asset | `Assign asset LAP-001 to Jane Doe`; ambiguous matches require selection; reassignment requires a bound preview/confirmation |
| Create employee directory record | `Create employee Jane Doe`; requires the existing settings permission; does not create a login account |

The backend applies ordinary business validation and live company/permission checks. Task state, business changes and retry receipts commit atomically. The UI preserves an unresolved exact request envelope across reload and chat-history refresh; retry the same pending action rather than starting a separate creation. A completed result contains a link to the actual record. Brie asset actions do not automatically create documentation.

Not implemented: general record editing, bulk actions, account management, Documentation writes or AI documentation drafts, and arbitrary actions in other modules. Employee directory records are not user accounts.

## Start the existing workspace

Keep the existing private `.env`, database and localhost certificate configuration. Do not start a second backend while the verified published instance owns port 5081. For a development session after intentionally stopping that instance, run from the project root:

```powershell
.\scripts\Start-Clary.ps1 -Provider Ollama
```

In another terminal:

```powershell
Set-Location frontend
npm.cmd run dev
```

Open `http://localhost:5174/` and sign in normally. `Start-Clary.ps1 -Compose` is intentionally unsupported for this HTTPS identity setup. Normal startup is not migration/bootstrap mode. The existing Ollama endpoint/model configuration is private server `100.79.42.5:11434`, `qwen3:8b` for generation and `embeddinggemma` for embeddings; this runbook does not install models or change the server.

Health: `/api/health` should report `ok`, `Live Ollama`, `liveReady: true`. Health confirms readiness, not answer correctness. Anonymous `/api/brie/capabilities` should return 401.

## Run automated checks

Use separate per-project artifacts to avoid stale/shared assembly collisions. Set `CLARY_TEST_CONNECTION` privately to a disposable local PostgreSQL/pgvector test database before running database fixtures; never echo credentials. Fixtures create and remove unique schemas. From the project root, the affected suite used for this release is:

```powershell
$classes = @(
  'AtomicSourceActionTests', 'BrieBoundaryTests', 'BrieConversationContextTests',
  'BrieFollowupTests', 'BrieGroundingTests', 'BrieInventoryTests',
  'BrieIssuePersistenceTests', 'BrieParserTests', 'BrieTicketTests',
  'CitationReferenceTests', 'CompanySessionTests', 'DirectoryIntegrationTests',
  'InventoryIntegrationTests', 'KnowledgeKeywordTests', 'OllamaProviderTests',
  'OrchestrationTests', 'QuoteVerificationTests', 'TicketTests'
)
$filter = ($classes | ForEach-Object { 'FullyQualifiedName~' + $_ }) -join '|'
& "$env:USERPROFILE\.dotnet\dotnet.exe" test backend/Clary.Tests/Clary.Tests.csproj `
  --artifacts-path artifacts/brie-runbook-tests -p:UseSharedCompilation=false `
  --filter $filter --logger 'trx;LogFileName=brie-runbook.trx'
```

The action/browser boundary tests intercept API requests; they do not create real company records. With Vite running and the existing Playwright browser installed:

```powershell
Set-Location frontend
npx.cmd playwright test tests/brie-actions.spec.ts tests/brie-boundaries.spec.ts --reporter=line
npm.cmd run build
```

These mocked browser checks are distinct from the existing signed-in user's browser.

The opt-in actual-model harness is retained on this computer at `C:\Users\sapro\Documents\Codex\2026-10-10\task\live-acceptance`. Its companion `Run-BrieCandidateAcceptance.ps1` verifies the staged DLL hash and loads that exact binary into an authenticated TestServer with synthetic users/documents and disposable database records. It uses Ollama and consumes server compute; it does not exercise the live signed-in browser. To repeat the verified candidate intentionally:

```powershell
Set-Location 'C:\Users\sapro\Documents\Codex\2026-10-10\task'
.\Run-BrieCandidateAcceptance.ps1 `
  -CandidateDll 'C:\Projects\Apps\Clary-Lab\artifacts\brie-current-candidate\publish\Clary.Api\release\Clary.Api.dll' `
  -ExpectedHash 'C389CF486DDF47DA8EAA7F3B21CFCD4AA998135732E996407ABCFB9F83F75E0E'
# Add -ActionsOnly for the 18-check action smoke.
```

The test-only harness is a local verification artifact, not a portable project test target. A later candidate requires a rebuilt compatible harness and its own reviewed hash; do not relabel older results as validation of new source.

## Verified release and evidence

- 186/186 affected backend checks, 10/10 mocked browser checks, frontend build passed.
- Independent review found a clarification-stage topic switch bug; it was fixed, covered by regression and re-reviewed without a remaining blocker.
- 69/69 real-model checks passed on the exact Release DLL; 18/18 synthetic action smoke checks passed after activation.
- API and Vite healthy, anonymous capabilities 401; both data scopes unchanged before/after restart.
- Global: 53 documents, 2 assets, 3 employees, 2 tickets. UNIO - Test: 24 documents, 0 assets, 2 employees, 1 ticket.
- Deployed DLL SHA256: `C389CF486DDF47DA8EAA7F3B21CFCD4AA998135732E996407ABCFB9F83F75E0E`.
- Source fingerprint: `068CF6FB2C86A4D93031BBBAEAB8F740BA5291D19ED517704612AAC2D706DE51`.
- Binary rollback: `artifacts/brie-conversation-rollback-20261010-235041`. This activation made no migration, model/config changes or real-company test writes.

See [verification history](brie-verification-2026-10-10.md), `artifacts/brie-current-candidate/verification-manifest.json`, and `artifacts/brie-test-results/brie-verified-final.trx`. Actual-model evidence is in the local task directory: `live-acceptance-final-release-pass.json` and `live-actions-postactivation-pass.json`.

The 186 checks are an affected suite, not a green result for every repository test. Five earlier broader-suite failures were reproduced with pre-Brie Inventory code: `DatabaseStartupTests.FreshBootstrapCreatesCurrentSchemaAndCannotBeRepeated`, `CompanyInventoryIsolationTests.OtherCompanyIdsAreInvisible`, `AssetConfigurationValueTests.CustomTypeAndValuesAreReturned`, `CompanyMigrationTests.LegacyDataKeepsIdsAndRelations`, and `CompanyMigrationTests.CompanyUniquenessAndChildReferencesAreScoped`. Their baseline evidence is `task/brie-results/pre-brie-baseline.trx` in the local task directory above.

## Remaining limits and browser verification

Natural-language interpretation and retrieval can still miss information or need clarification. Unnumbered multi-action prose can be conservatively withheld in favor of requesting a clearer approved procedure. Grounding controls do not eliminate hallucinations or certify the correctness of approved documents. The known-result omission rules are narrow, not a general semantic understanding guarantee.

Signed-in production-browser end-to-end acceptance is still unverified. The bounded supported initialization attempt on 2026-10-11 failed before obtaining a browser session:

```text
node_repl kernel exited unexpectedly
kernel_status: exited(code=1)
MXC launcher: cannot locate required Windows platform directory: HRESULT 0x80070003
reason: stdout_eof
```

This is a managed browser-runtime startup/path failure, not a Clary authentication/access denial. No alternate browser-control route, credential extraction or security-setting change was attempted. The next supported tooling step is repair/reinitialization of the Codex managed browser runtime through its supported setup/support path, then a fresh initialization attempt; recovery has not been verified.

A user can independently refresh the existing normal signed-in UNIO - Test tab, send `Hi`, and ask a documented question, opening its citation. This requires no record creation. Production action smoke should only be done intentionally through the ordinary UI, not inferred from synthetic tests.