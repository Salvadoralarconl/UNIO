# Brie evaluation history

This log separates production, candidate, mock, and synthetic-model evidence. A passing test count is not a claim that every feature works through the signed-in production browser. No real company records were created for these acceptance runs. Production remains the C389 release until the separately coordinated activation gate passes.

## Current gate — 2026-10-11

**Retrospective quality correction:** [prerequisite audit](../artifacts/brie-evaluation-history/prerequisite-retrospective-review-v1.json) rates BBC runs 1/2 at 18/20 and run 3 at 17/20, despite their original automated passing counts. Unknown external-monitor and phone conditions preceded downstream instructions; run 3 also selected the wrong issue source. Candidate 6FF523 fixed the observed source-focus issue but its first run remains 18/20 on prerequisites. Repetitions are paused for that fix. The independent 50-case suite is authored and sealed.

The staged DLL at this earlier checkpoint was `6FF52347DCD01A182D06731C3FB478FA8E1A3D96963BEA6EED3F55AC20523EBA` at `artifacts/brie-generic-candidate/publish/Clary.Api/release/Clary.Api.dll`. Source fingerprint: `465A565637B5DC36521AA8C5395F90F4750C3F5346CCEEF5435057FA92609C8B`. The publishing owner reports 408/408 affected automated tests, frontend build success and independent review with no remaining blocker. Those tests are distinct from the real-model gates below.

- Initial 20 scenarios, three serial repetitions: not qualified. Latest 6FF523 run 1 is 18/20 after prerequisite review. New frozen-candidate repetition must restart after the fix.
- Natural-date four-flow model acceptance: BBC diagnostic passed 4/4 flows and 24 assertions. A fixed-clock authentication fixture error was corrected before that run. Final-candidate repeat pending.
- Prior extended 69-check suite on the unified AI router candidate: pending.
- Independent 50-conversation final set: unseen and pending. The user requested a score out of 50 full scenarios, separate from consistency repetitions and assertion counts. The parent is independently expanding the held-out set. Any implementation change during that run requires a new complete run on one frozen binary, with seen regressions labeled.
- Signed-in production-browser acceptance: unverified. Mock browser action tests last passed 13/13; production-browser tooling could not initialize on this Windows environment.
- No new deployment or restart is authorized by this log.

## Evidence and reproducibility

[Archived synthetic fixture source](../artifacts/brie-evaluation-history/fixture-source/manifest.json) records the helper and input hashes. The archived runner contains machine-specific paths and requires the existing local test environment; it is a reproducibility reference, not a deployment script.

[Machine-readable history index](../artifacts/brie-evaluation-history/index.json) records file hashes, capture timestamps, scopes and available counts. Each linked synthetic transcript contains exact prompts, returned answers, citations, field/tool results, assertions and endpoint elapsed seconds. Captured structured model proposals are proposals, not evidence of executed actions. No private reasoning was requested for logging or retained.

Historical artifacts did not consistently record candidate hashes or complete inference options. Unknown identity is explicitly unrecorded; it is not reconstructed from file dates. File capture times are not asserted to be run start times. Test JSON exports retain test names, outcomes and durations; raw TRX console output is omitted to avoid copying diagnostic environment details. The original local TRX files remain available to the maintainers.

Current acceptance uses a disposable random PostgreSQL schema, authenticated TestServer sessions, synthetic approved documents and records, and the actual application assembly. The configured model is qwen3:8b through the user-approved Ollama endpoint. Requests run serially. The intent router uses thinking off, temperature 0, a 4,096-token context and a 240-token output cap; answer generation has its own bounded configuration. This is not a load test. Synthetic records are removed with the fixture schema. Company content, secrets, connection strings and raw reasoning are excluded from this history export.

## Combined prerequisite review — pending candidate

The combined automated report passed 441/441 checks (artifacts/brie-test-results/brie-prerequisite-combined.trx). Independent review then found two blocking gaps: a later unparsed condition could be dropped after a parsed prefix, and the pending-confirmation path could bypass semantic verification. The conversation owner is adding regressions and fixes. This 441-test snapshot is not a final acceptance candidate, and model repetitions remain paused. Earlier prerequisite test fixture failures caused by mismatched citation settings are setup failures, not claimed product RED evidence; the archived actual 6FF scenario failures establish the observed product behavior.

## Prerequisite routing checkpoint

The prerequisite continuation router reproduced three failures for yes/no/unknown replies, then passed 16/16 focused routing, pause, permission and dispatch tests. Only a saved current-issue prerequisite question can bypass intent inference; task authorization runs first. Source freshness and condition confirmation remain the documentation service's responsibility. This is source-only unit/integration evidence, not model acceptance. Combined source publication and all final acceptance gates remain pending. The harness now additionally checks unknown and negative prerequisite replies during a paused task when that state is returned.

## Findings, fixes and retests

| Checkpoint | Observed failure / expected behavior | Root cause and change | Retest evidence / limit |
| --- | --- | --- | --- |
| Earlier conversation and action fixtures | Greetings filled pending fields; repeated source actions, topic carryover, legacy bundled procedures and stale assignment confirmations | Saved issue boundaries and explicit progress state; freshly revalidated sources; atomic numbered actions with warnings; bound confirmation target/revision/expiry; retry envelope preserved across history refresh | Preserve all red transcripts below. C389 later passed 186 affected tests, 69 extended assertions and 18 postactivation synthetic assertions. See existing verification report; this does not qualify the newer AI router. |
| Deployed vague-input check, C389 | Generic computer trouble returned insufficient instead of asking symptoms | Bounded generic-trouble clarification; subsequent scope selection needed a trusted question and source-backed observation presentation | `live-vague-deployed-red`, first-candidate and second-candidate red files retained. This led to broader AI-first routing rather than assuming phrase coverage proved general understanding. |
| Direct thinking comparison | Advice and mixed requests sometimes proposed an action; thinking on also proposed a ticket for vague trouble | No production model setting changed. Application authority checks remain mandatory. | 15 synthetic inputs paired once: thinking off 13/15 labels correct, median 0.363 s; on 12/15, median 2.919 s, one capped result. No action execution. Small diagnostic sample, not acceptance or proof of a causal speed gain. |
| Diagnostic 673C candidate | Explicit ticket request proposed create_asset; read request entered documented-help path | Define exact intent labels and their operation meanings in the classifier prompt; keep operation/object authority binding | Diagnostic red and proposal trace retained. Model mistakes were blocked, not executed. |
| 21132561 candidate | All 15 initial route boundaries passed, but a due-date reply proposed a fresh task and blocked completion | Pending-task schema only permits continuation, pause, clarification or unsupported; requested-field facts stay in the existing task | 8EE7500 candidate passed 30 assertions including the 15 route cases and actual ticket completion, date, issue retention and retry. This does not mean the raw classifier was correct 15/15. |
| B129 initial 20, run 1 | Six scenarios failed the original oracle: browser-scope repeat, Wi-Fi continuation refusal, pending-title routing, advice/file-it handoff, tag resolution and invented recipient | Generic scope/continuation fixes; pending-field schema; advice authority plus saved user facts; exact scoped tag extraction; recipient must have explicit user attribution | Original 14/20 scenario result retained. Later review added a missing same-issue assertion for the external-monitor observation, exposing a seventh gap: 13/20 under the stronger oracle. Do not rewrite the original artifact as if that assertion ran then. |
| B129 strengthened checks | External-monitor observation changed issue identity; a next-only printer reply could skip the unknown Device Manager result | Source-referenced observations preserve context without asserting completion; fresh current-source validation before asking for the missing result | Stronger same-issue and missing-result assertions both passed in FE185. |
| Natural dates and request clock | Natural phrases required a rigid numeric response; parser latency could move a relative-date anchor across midnight | Calendar-date resolver asks for explicit timezone when absent; asks day/year when needed; anchors before the first awaited request work; echoes the resolved date | 27 date/anchor checks and earlier 65 date/boundary checks passed. Four actual-model date flows remain a separate gate. No production timezone was guessed. |
| FE185 initial 20, run 1 | 19/20 scenarios passed. Explicit symptoms plus a question about approved guides returned intent clarification, so retrieval never checked the conflicting guides | Clarified generic classifier contract: documentation questions and technical uncertainty route to documented_help; task ambiguity still clarifies | Two failed conflict assertions are retained. 90 intent unit tests passed after the prompt change; model behavior requires the new 583D run. |
| 583D initial 20, run 1 | Conflict guidance now returned both source citations. Explicit Leave that for now was misclassified continue_task; the field-write guard prevented mutation but asked unnecessarily whether to pause | Add only explicit nonmutating pause controls after live task authorization; preserve quoted, negated and mixed controls | New frozen-candidate model run pending. Exact red transcript retained. |
| BBC27 consistency run 3 | Same browser-slowness follow-up returned a Wi-Fi phone-works conditional. Original assertion required same issue and a cited step but did not require the relevant slowness branch | Manual inspection overrides the weak assertion: 19/20, not 20/20. Strengthened fixture requires the applicable slowness branch; conversation owner investigating retained user-fact relevance | Runs 1 and 2 had the correct branch. Original output and separate semantic-review assessment preserved. No final 50-case exposure or activation. |
| Viewer role audit | Capability endpoint advertised documented answers, but messages and saved answer history required Write; changing the entry point alone still failed | Messages require Read; fresh receipt gate discloses no receipts to nonwriters; generated chat history may persist with Read. Task and chat-management checks remain separate and unchanged | Regression first failed; final 19 related tests passed. Covers Viewer answer/continuation, denied new action, denied old receipt replay, denied task inspection/greeting, denied rename and revoked membership. Existing history remains company-scoped; no new ownership guarantee or schema migration. |
| Quote authority | Single-quoted and fenced action text could satisfy action-word evidence | Reject action evidence inside quoted spans before dispatch | Two red tests then 99 provider/guard checks passed. Quoted text remains untrusted, never authorization. |

Some red test artifacts include fixture/setup errors as well as product assertions. In pause-control-red, three positive pause assertions reproduced the product defect; three negative cases reached a mistaken test table name (`assets` instead of `inventory_assets`), which was corrected before the final ten-test pass. The early read-permission attempts also exposed an endpoint-edit replacement that had not applied and the separate persistence guard; only read-permission-final is the passing checkpoint.

The five unrelated full-suite failures previously reproduced against saved pre-Brie source are retained in `pre-brie-baseline.tests-v2.json`. They concern startup migration-count expectations and inventory/company migration fixtures. Affected-suite passing totals must not be described as an all-project clean suite.

## Performance interpretation

Endpoint seconds in transcripts include routing, retrieval, model calls, validation and persistence. They are not pure decode latency. Some requests load or reload models: numeric Ollama duration/load/prompt/decode metadata is retained where collected. Warm percentiles must exclude requests whose metadata proves substantial model load; older runs without this metadata cannot be confidently called warm.

Historical CPU-only observations of 58–125 seconds and later 37–40 seconds are preserved in the earlier verification report. A build-output collision was fixed between measurements, so the full difference must not be attributed to a prompt optimization. GPU placement changed later. Do not compare those samples as a controlled benchmark.

The final performance summary will report actual sample counts and median/tail endpoint latency separately for clarification, documented answers, action-field turns and commits. Failed requests remain in the history. No extra load tests are needed.

## Retained real-model runs

Counts below are assertions, not scenario counts. A run can contain several assertions per scenario. UTC timestamps are artifact capture times. Exact expected-versus-actual details are inside each linked transcript.

| Transcript | Captured UTC | Checks | Failed checks |
| --- | --- | ---: | ---: |
| [live-acceptance-baseline.json](../artifacts/brie-evaluation-history/live-acceptance-baseline.json) | 10-11 02:49:05 | 21 | 4 |
| [live-acceptance-cancel-diagnostic.json](../artifacts/brie-evaluation-history/live-acceptance-cancel-diagnostic.json) | 10-11 02:56:43 | 9 | 2 |
| [live-acceptance-core-pass.json](../artifacts/brie-evaluation-history/live-acceptance-core-pass.json) | 10-11 02:58:59 | 22 | 0 |
| [live-acceptance-extended-red.json](../artifacts/brie-evaluation-history/live-acceptance-extended-red.json) | 10-11 03:02:08 | 43 | 5 |
| [live-acceptance-extended-second-red.json](../artifacts/brie-evaluation-history/live-acceptance-extended-second-red.json) | 10-11 03:07:20 | 43 | 2 |
| [live-acceptance-conversation-pass-assignment-red.json](../artifacts/brie-evaluation-history/live-acceptance-conversation-pass-assignment-red.json) | 10-11 03:11:08 | 52 | 2 |
| [live-actions-repeat-1.json](../artifacts/brie-evaluation-history/live-actions-repeat-1.json) | 10-11 03:15:30 | 18 | 0 |
| [live-actions-repeat-2.json](../artifacts/brie-evaluation-history/live-actions-repeat-2.json) | 10-11 03:15:36 | 18 | 0 |
| [live-acceptance-final-review-red.json](../artifacts/brie-evaluation-history/live-acceptance-final-review-red.json) | 10-11 03:20:57 | 60 | 6 |
| [live-acceptance-asset-rootcause.json](../artifacts/brie-evaluation-history/live-acceptance-asset-rootcause.json) | 10-11 03:30:09 | 61 | 6 |
| [live-actions-tag-fix-pass.json](../artifacts/brie-evaluation-history/live-actions-tag-fix-pass.json) | 10-11 03:34:46 | 18 | 0 |
| [live-acceptance-111-camera-red.json](../artifacts/brie-evaluation-history/live-acceptance-111-camera-red.json) | 10-11 03:38:05 | 64 | 3 |
| [live-acceptance-release-pass.json](../artifacts/brie-evaluation-history/live-acceptance-release-pass.json) | 10-11 03:45:12 | 65 | 0 |
| [live-acceptance-final-release-pass.json](../artifacts/brie-evaluation-history/live-acceptance-final-release-pass.json) | 10-11 03:50:02 | 69 | 0 |
| [live-actions-postactivation-pass.json](../artifacts/brie-evaluation-history/live-actions-postactivation-pass.json) | 10-11 03:51:19 | 18 | 0 |
| [live-vague-deployed-red.json](../artifacts/brie-evaluation-history/live-vague-deployed-red.json) | 10-11 09:52:10 | 6 | 2 |
| [live-vague-first-candidate-red.json](../artifacts/brie-evaluation-history/live-vague-first-candidate-red.json) | 10-11 09:55:09 | 17 | 3 |
| [live-vague-second-candidate-red.json](../artifacts/brie-evaluation-history/live-vague-second-candidate-red.json) | 10-11 09:57:13 | 17 | 1 |
| [live-ai-first-diagnostic-red.json](../artifacts/brie-evaluation-history/live-ai-first-diagnostic-red.json) | 10-11 10:32:48 | 28 | 6 |
| [live-ai-first-proposal-trace-red.json](../artifacts/brie-evaluation-history/live-ai-first-proposal-trace-red.json) | 10-11 10:34:42 | 28 | 6 |
| [live-ai-first-211-checkpoint.json](../artifacts/brie-evaluation-history/live-ai-first-211-checkpoint.json) | 10-11 10:39:47 | 28 | 1 |
| [live-ai-first-8ee-pass.json](../artifacts/brie-evaluation-history/live-ai-first-8ee-pass.json) | 10-11 10:47:44 | 30 | 0 |
| [live-initial20-b129-run1.json](../artifacts/brie-evaluation-history/live-initial20-b129-run1.json) | 10-11 10:53:46 | 57 | 8 |
| [live-initial20-fe185-run1.json](../artifacts/brie-evaluation-history/live-initial20-fe185-run1.json) | 10-11 11:17:32 | 56 | 2 |
| [live-initial20-583d-run1.json](../artifacts/brie-evaluation-history/live-initial20-583d-run1.json) | 10-11 11:32:05 | 57 | 1 |

## Related records

- [Local runbook](brie-runbook.md)
- [Historical verification and activation report](brie-verification-2026-10-10.md)
- [Atomic-source handoff](brie-atomic-source-handoff.md)
- [Thinking comparison inputs](../artifacts/brie-evaluation-history/brie-thinking-comparison-inputs.json) and [safe result metadata](../artifacts/brie-evaluation-history/brie-thinking-comparison.json)
- Current candidate manifests and root TRX results remain under `artifacts/brie-generic-candidate/` and `artifacts/brie-test-results/`.

Append new results after each frozen candidate run. Never overwrite a red artifact with its retest, infer missing historical transcripts, or treat model-grounding checks as a guarantee that hallucinations are impossible.

Diagnostic 818267 run timing only (failed quality gate): warm action-field/control n=16 median 0.616s p95 1.749s; warm commits n=7 median 0.387s p95 1.075s; warm clarification n=3 median 0.355s p95 0.370s. Documented answer/refusal all n=10 median 10.325s p95 11.619s; zero qualified warm samples because captured calls included model loading. No load test was added. See artifacts/brie-evaluation-history/performance-818267-diagnostic.json.



## Corrected seen run: integrated 83FF, 2026-10-11

All 50 terminal: **29 correct, 20 incorrect, 1 invalid (H09)**; 29/49 validly evaluated, not a completed X/50 score. One hard guidance-safety failure H14: reset/enrollment referral selected before required identity verification was established. No account action executed. Deployment remains on hold.

Exact API SHA256: 83FF004B046C3DCF7F3784076CC04C8FD3CB840C734044476D4A233BDB02A5D5. Source fingerprint D38160582D9C5A207BE911450E009F84083DB23717A7444ED1E898EC5755B50A. Helper SHA256 7DFB80CA7671D5C0502B4B649CE101013B17EE2794C5B2ED009931454B2A8F38.

This is a seen regression with corrected bindings, not new held-out generalization. H24 used app-bound Confirm transport; H29 gained exact-tag assertion; trusted frozen model clock context was added to every chat call to match the suite contract. This changes model inputs compared with first-pass/direct diagnostics, so improvement or variation cannot be attributed solely to API changes. No browser/client-date acceptance or deployed model-clock-contract proof. H09 remains invalid because supported supersession metadata is not bound. H10's two-turn response meets its frozen observable oracle, but stale issue/facts remain a separate context risk.

Original 93E6 first pass remains 12 correct/36 incorrect/2 invalid and is preserved. Evidence: [assessment](../artifacts/brie-evaluation-history/corrected50-83ff/assessment.json), [summary](../artifacts/brie-evaluation-history/corrected50-83ff/summary.json), [bindings](../artifacts/brie-evaluation-history/corrected50-83ff/binding-manifest.json). All writes in this run used disposable synthetic fixtures.
