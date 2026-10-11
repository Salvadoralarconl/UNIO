# Company billing profile addendum

Date:2026-10-08. Proposed bounded extension to the approved Operator company-management flow. Payment provider remains undecided; account-management implementation continues independently.

## User intent
When the Operator creates a company and its Administrator (called manager by the user), include Company name and billing details. User also requests actual payment collection, but has not selected a provider. Do not treat Administrator as an additional Manager role.

## Billing profile now
Extend the existing atomic company+Administrator setup and Operator company-detail edit with billing contact name/email, billing address (lines, city, region, postal code, country), optional tax ID, agreed subscription price, currency and billing cycle (Monthly/Annual). Company name already exists and remains required. Profiles may be saved incomplete as drafts, clearly indicating missing fields. Required fields for a ready profile: contact name/email, country, currency, price and cycle; other address/tax fields are optional until chosen processor rules apply.

Store price exactly as a decimal string/NUMERIC(20,4), never binary floating point; currency uppercase three-letter code. This records an agreement, not a successful charge or tax determination. Do not collect card numbers, security codes or bank credentials in Brie. Provider configuration and pricing validation will belong to the payment integration. Profile edits require expected revision, Operator authority revalidation and immutable account audit excluding secrets. Company Administrator may read its own billing summary; Editor/Viewer cannot access billing profile. Only Operator may edit. Do not alter existing seat accounting or enablement automatically.

Frontend uses the same white rounded panels, custom dropdowns, aligned fields and black primary actions. Company setup groups Company, Administrator, Subscription/seats and Billing. Show payment status 'Not connected' until a provider is configured; never simulate Paid. Existing companies without profiles remain valid. Saving company+Administrator+billing draft is atomic if profile supplied, so failures do not leave partial setup.

## Payment collection later
Implement real provider-hosted checkout and account-specific billing portal after provider selection and design. Preferred option is Stripe, but no provider has been approved. Integration needs server-only credentials, trusted webhook signature verification, idempotent duplicate/out-of-order event handling, server-side customer/company binding, and explicit pricing/currency/catalog configuration. Do not activate charges, send payment requests or create external customers during local development without the intended test/live configuration. Billing status and seat entitlement changes must have defined policy; failure should not silently delete/disable users. Operator retains subscription/user-limit authority; customer portal must not permit unauthorized seat-plan changes.

## Verification
Backend Operator-only mutations, Administrator own-summary read, Editor/Viewer/foreign-company denial; exact amount preservation, stale revision conflict, atomic create rollback, unchanged seat usage, audit. Isolated browser form save/error/draft, mobile aligned layout, company name, Not connected status and no card-entry fields. Keep actual payment integration reported pending, not complete.

## Placement
After seat/account service review, implement profile as a small reviewed extension before Operator dashboard UI. Append migration after current numbered migrations; no existing checksum edits. Add dedicated company_billing_profiles table keyed/company-scoped, separate from entitlement subscription. Account plan Task5 consumes profile DTO/route only after this extension approval and implementation. Payment subsystem gets its own design/plan once provider selected.
