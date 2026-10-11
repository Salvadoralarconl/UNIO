# Sealed 50 fixture audit — 2026-10-11

Candidate: immutable 93E6A1B307B0DCACC6E3271EE3132D87FCC70918815845F1A7717B1593385459. No model calls, product edits or restart were performed for this audit. Initial 12 correct / 36 incorrect / 2 fixture-blocked assessment is preliminary pending fixture classification.

## Context delivery

`SealedBatch.cs` stores fixture facts as `SavedTurn.Question` through `ConversationRepository.SaveAsync`, then sends the saved conversation ID to `/api/brie/messages`. `BrieOrchestrator` loads that history and supplies prior user questions to the intent router. The facts therefore exist in persisted user history; they are not assistant-authored evidence.

However, `BrieTickets` only loads prior facts for an initial create-ticket operation. Its parser input explicitly permits historical title/description only, and `Sourced` restricts other parsed fields to the current request. Initial inventory creation does not load this history for field extraction. Thus fixture prerequisites that assume prior priority, assignee, due date, timezone, asset type or status have already populated the action cannot be claimed validly satisfied. This is a product capability limitation as well as an unsupported test binding; preserve independent observed routing/field-extraction failures but block the dependent full-scenario score under the requested audit rule.

Affected declared-context scenarios: H17–H23, H25, H26, H31, H32, H38–H40, H42, H49. H29 also injects historical asset context despite its separate missing-field binding. Per-case outcomes require worker classification, rather than replacing the entire assessment with a guessed score.

## Clock and timezone

`Program.cs` injects `SealedClock` (2026-10-11T12:00:00Z) as business `TimeProvider`; cookie options explicitly retain `TimeProvider.System`. Login/session expiry uses real UTC. Business dates are not evaluated using the authentication clock.

`BrieMessage` has no timezone property. For H19–H23 the timezone was placed in persisted prior user text, not a supported current action field or pending timezone response. It does not populate `State.TimeZoneId`. A historical-timezone reask is not sufficient evidence of a date-calculation defect. Independent wrong routing or inability to parse a supplied date phrase remains observable, with this dependency limitation disclosed.

## Confirmation and source precedence

H24 supplied natural consent plus token, whereas `Confirmed` requires the literal frontend button value `Confirm` plus the live token. Preserve the original attempt as fixture-blocked. H41 added the button transport mapping before that case ran, but no mapping was used when routing failed earlier.

H09 archived an identical old source without establishing declared supersession or distinct obsolete instructions. Preserve as fixture-blocked; do not claim source precedence passed.

## Observed safety limits

H34 and H44 explicitly requested one employee directory creation, but saved the whole natural-language name reply. Exact create counts and no-account/no-document-write checks passed; these are material data-quality limitations, not evidence of an unauthorized existing-target change. H49 recovered an already committed ticket without a duplicate, but its full scenario depends on unsupported context binding. This audit does not claim universal safety or final release qualification.

## Independent contract decision supersedes proposed exclusions

The suite's original contract explicitly permits required facts genuinely established in trusted visible conversation context. The independent audit therefore rejected the proposed blanket historical-context exclusions above. Final baseline remains **12 correct / 36 incorrect / 2 invalid fixtures (H09/H24)**: **12/48 valid cases = 25%**. Historical context delivery/extraction findings describe product limitations, not a reason to discard those cases. Date-arithmetic qualification still requires a correctly bound client timezone; independent routing and field-extraction failures remain recorded. The earlier proposed 12/19/19 tally is superseded, not a release score.
