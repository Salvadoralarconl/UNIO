# Brie verification — October 10, 2026

Activated the coordinated retrieval and Inventory/Tickets action changes. The API is running from `artifacts/brie-activation-final/Clary.Api.dll` with the existing qwen3:8b model. No model fine-tuning or new model installation was performed.

## Document answers

Correct workspace: UNIO - Test. The user explicitly authorized retrieved documentation to be sent to the trusted configured Ollama endpoint. Current approved, indexed sources remain company scoped and permission checked.

Fixed short-document procedure hydration, Markdown heading/block selection, informal-query keyword fallback, and topic changes between questions. Reduced duplicate model context while retaining evidence verification and citation checks. Insufficient answers do not show unrelated diagnostic questions.

The first live six-question service evaluation returned cited answers for account sign-in, offboarding, webcam troubleshooting, printer troubleshooting, and onboarding. It refused to invent the company Wi-Fi password. The final isolated build repeated the original sign-in/offboarding problems with correct citations in 39.6 and 36.6 seconds. CPU inference remains a material latency limitation. An installed smaller-model comparison reached its output limit; it was canceled and not adopted.

## Actions

Available bounded operations: create Support ticket, cancel an eligible Support ticket, create asset, assign asset, and create employee directory record. Existing business validation and permissions apply. Missing fields, ambiguous choices, confirmation, transactional receipts, and retry isolation are covered by fixtures. Documentation writes, login-account creation, bulk actions, and general record editing are not implemented through Brie.

The final live synthetic parser evaluation extracted the supplied ticket, employee, and asset fields correctly in 23.2, 4.5, and 13.8 seconds. It created no records. These parser timings do not represent full action-flow timings.

## Verification and activation

- Final affected backend suite: 85 passed, zero failures, using separate per-project build outputs. Results: `artifacts/brie-test-results/brie-affected-isolated.trx`.
- Frontend production build passed. Coordinated isolated browser tests covered action confirmation, choices, retries, record links, refresh persistence, and company switching.
- Broader backend run had five preexisting fixture failures. They were reproduced with the original Inventory repository and migration 018 omitted. A sixth stale-build parser failure passed with isolated build outputs.
- API/database backup: `artifacts/brie-rollback-20261010`. Actual restoration to a separate database succeeded, preserving 53 documents and migration version 17.
- Migration 018 rehearsed successfully on the restored database, preserving 53 documents. It adds only task and receipt tables.
- Live activation completed in about five seconds. Post-activation health reports `ok`, `Live Ollama`, and `liveReady=true`; migration version is 18; document count remains 53; no live test task records were created. An anonymous capabilities request returns 401 as required.
- A coordination hold arrived after activation. This timing was reported to the other chat and the user. Subsequent approval arrived; no second restart was performed.

## Remaining acceptance limitation

The supported browser tool failed initialization with a sandbox permission-path error. Therefore signed-in end-to-end UI acceptance in the actual user account is not verified. No authentication credentials were extracted or forged, and no real tickets/assets/employees were created for testing. Refresh the app and check a documented question in the signed-in workspace; any real action smoke test should be performed intentionally through the normal UI.

Deployed API SHA256: `7A2889BDBD8343B4212E3B34B1DCA65014082FE71CCB24B4F2B6FF6ADE673F10`.
Database backup SHA256: `FD70EE33CBA84B7BB9C62661997D5580F965C1146791301D0161431343BCC53C`.

## Activated reviewed conversation/action candidate

The final Release passed186/186 affected backend tests, independent review and rereview, and69/69 real-model assertions using an authenticated isolated synthetic TestServer fixture. The harness DLL and deployed DLL match SHA256 C389CF486DDF47DA8EAA7F3B21CFCD4AA998135732E996407ABCFB9F83F75E0E; source fingerprint068CF6FB2C86A4D93031BBBAEAB8F740BA5291D19ED517704612AAC2D706DE51. Guarded binary-only activation succeeded as PID31920, rollback artifacts/brie-conversation-rollback-20261010-235041. API and Vite proxy both report ok/Live Ollama/liveReady; anonymous capabilities returns401. No migrations or real fixture mutations were performed. Global counts remain docs53/assets2/employees3/tickets2; UNIO - Test remains docs24/assets0/employees2/tickets1. The approved synthetic postactivation smoke is pending; an actual signed-in browser session remains unverified.
Postactivation exact-DLL real-model synthetic action smoke passed18/18 with zero failures. Source manifest remains unchanged. Artifact C:/Users/sapro/Documents/Codex/2026-10-10/task/live-actions-postactivation-pass.json. Verification is complete for the bounded scope; production signed-in browser interactions remain unverified.
