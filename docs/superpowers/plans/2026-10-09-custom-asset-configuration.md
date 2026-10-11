# Custom Asset Configuration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Deliver company-specific asset types and typed custom fields through a new Configuration screen, preserving existing assets and kits; stop for user review after deployment.

**Architecture:** Stable company-scoped type IDs augment existing category codes. Field definitions have optional type scope; JSON values keyed by field ID are validated and audited with asset transactions. Configuration uses existing Settings permission and the current white Inventory design.

**Tech Stack:** ASP.NET Core10, PostgreSQL/Npgsql, React19/TypeScript, Lucide, xUnit, Playwright.

**Spec:** docs/superpowers/specs/2026-10-09-custom-asset-configuration-design.md

## Global Constraints
- Only feature1; feature2 waits for user review.
- Per-type fields with optional shared fields; kinds shortText,longText,number,date,boolean,choice.
- Preserve existing assets, categories, kits, request snapshots and file contents.
- Configuration mutations require persisted Settings permission, company ownership, CSRF, optimistic revision and transactional audits.
- Archive referenced definitions; do not delete existing values or definitions.
- No Git metadata exists: use private source snapshots and review records, do not initialize Git.
- No actual data fixtures, deletions or migration until reviewed matching rollout. SourceDesk untouched.

## Review Focus
- Renaming a type must not break kit allocation or legacy Computer monitoring.
- Archived required fields must not block asset saves; existing values remain readable.
- Foreign type/field IDs and unknown properties must not bypass tenant or protected-field validation.
- New required fields leave old records intact but require completion on the next applicable save.
- Switching company or type during an open form must not submit stale metadata or overwrite hidden historical values.

## File boundaries and contracts
Create backend/Clary.Api/Inventory/AssetConfigurationContracts.cs, AssetConfigurationRepository.cs, AssetConfigurationEndpoints.cs, AssetFieldValidation.cs. Register through backend/Clary.Api/Program.cs and CompanyContentEndpoints.cs. Add database/migrations/010-asset-configuration.sql only after rechecking ledger.

Types: AssetTypeDto(id,code,name,icon,active,sortOrder,revision), AssetTypeInput(name,icon,active,sortOrder,revision?). code is immutable; default codes remain Computer,Printer,Monitor,Network,Accessory; custom codes derive from stable UUID, never display name. AssetFieldDto(id,assetTypeId?,name,kind,helpText?,required,active,sortOrder,choices:string[],revision), matching AssetFieldInput. Configuration GET response {types,fields,icons:string[]}.

Endpoints /api/inventory/configuration GET; /asset-types POST and /{id} PUT; /asset-fields POST and /{id} PUT, under /api/inventory/configuration. GET requires company content read or assigned-content read metadata only; mutations Settings. No physical definition DELETE in feature1.

Assets add assetTypeId,assetTypeName,customFields JSON keyed by field UUID; legacy category remains immutable code for compatibility. EquipmentLine adds optional assetTypeId; existing category retains stable code. Requests retain historical snapshots and old JSON unchanged.

Limits:100 active types/company;100 active fields/company;name100/help500;choices1–100 unique trimmed values up to100 each;shortText200,longText4000;numeric finite decimal;date ISO YYYY-MM-DD;boolean true/false;customFields JSON maximum64KB. Reject unknown/foreign field IDs; retain existing archived/historical values unless explicitly updating applicable fields. Field kind and scope cannot change once referenced values exist.

### Task1: Persistent configuration, defaults and secure CRUD
**Files:** create configuration backend files and010 above; modify Accounts/CompanyAccountRepository.cs new-company creation, Program.cs and Knowledge/CompanyContentEndpoints.cs; tests backend/Clary.Tests/AssetConfigurationTests.cs.
**Produces:** AssetConfigurationRepository.GetAsync(context,ct), SaveTypeAsync(context,id?,input,ct), SaveFieldAsync(context,id?,input,ct), SeedDefaultsAsync(connection,transaction,companyId,ct). DTO/route contracts above.
- [ ] Write failing tests ConfigurationSeedsDefaultsAndPreservesLegacyData, ConfigurationRejectsForeignIdsAndNonSettingsActors, ConfigurationRevisionsAndAuditsRollback; assert five defaults, exact palette, archived rows and original asset/request snapshots unchanged.
- [ ] Run isolated dotnet test --filter FullyQualifiedName~AssetConfigurationTests; confirm feature absent RED.
- [ ] Implement010 company composite keys and default backfill; asset_type_id on assets; custom_fields JSON default{}; update kit JSON with stable IDs without altering request JSON. Add immutable code uniqueness and field/type FK ownership; seed new companies in existing transaction.
- [ ] Implement bounded DTO validation, allowlisted30 icons from spec, name uniqueness case-insensitive, revisions, lock order and audit rollback. Required field addition does not backfill arbitrary values.
- [ ] Run focused tests GREEN, save report and snapshot; independent reviewer checks migration/security before Task2.

### Task2: Asset values and configured kit types
**Files:** modify Inventory/InventoryRepository.cs, EquipmentContracts.cs, EquipmentRepository.cs, Knowledge/InventoryDocumentation.cs if needed; implement AssetFieldValidation.cs; tests AssetConfigurationTests.cs and EquipmentTests.cs.
**Consumes:** Task1 types/fields and SeedDefaults contracts. **Produces:** extended asset/kit DTOs and validation used by frontend.
- [ ] Write failing tests AssetValuesValidateAllSixKindsAndRequiredRules, ArchivedFieldsAndTypeSwitchPreserveHistoricalValues, RenamedCustomTypesKeepKitAllocationStable. Test finite numbers/invalid dates, unknown UUIDs, archived types on old versus new records and untouched historical snapshots.
- [ ] Run only new tests and confirm RED.
- [ ] Resolve legacy code or supplied type UUID against company type registry; compare kit stock by stable type/code, allow archived type on unchanged existing asset only. Replace fixed five-type validation, retain Computer-specific semantics by immutable code.
- [ ] Validate applicable values server-side within existing asset write transaction; merge shared/current type values without erasing hidden history; immutable field kind/scope after use. Return display name alongside stable code.
- [ ] Custom types may appear in Inventory/Kits; documentation controls for unsupported types are omitted with clear empty state, never issue invalid legacy folder route or fake documentation.
- [ ] Run focused inventory/configuration/equipment tests; independent review and report before frontend integration.

### Task3: Configuration screen and typed asset forms
**Files:** create frontend/src/assetConfigurationApi.ts, AssetTypeIcon.tsx, AssetConfiguration.tsx, CustomAssetFields.tsx, asset-configuration.css; modify App.tsx, InventorySettings.tsx, Inventory.tsx, ComputerAssetEditor.tsx, ComputerAssetDetails.tsx, equipmentApi.ts, EquipmentKits.tsx, inventoryApi.ts. Tests frontend/tests/asset-configuration.spec.ts and equipment-kits.spec.ts.
**Consumes:** exact Task1/2 DTOs and routes. **Produces:** usable Configuration navigation and configured asset/kit forms.
- [ ] Write strictmock browser tests ConfigurationNavigationMovesDirectories, CustomTypeIconPickerAndRename, TypedFieldsRequiredAndSharedValues, CompanyChangeDiscardsOpenConfiguration, ArchivedDefinitionPreservesOldDisplay, CustomTypeKitSelection. Confirm feature absent RED.
- [ ] Add SlidersHorizontal button immediately above Settings; common toolbar matches other tabs. Configuration contains Asset types, Custom fields, Departments, People, Locations, Assignment review. Settings keeps account/company/account management. Backend permission is authoritative.
- [ ] Icon grid uses exactly30 spec icons from Lucide, selected label and keyboard focus; no package dependency or uploaded SVG. White outlined cards, soft shadows, existing dropdowns and compact geometry.
- [ ] Load configured type cards/dropdowns including display names and icons; required typed field controls scoped to selected type plus shared fields; show values in asset details. Retain hidden historical values on type switch, cancel/abort metadata and mutation on company/session change, clear stale409 drafts with explicit reload.
- [ ] Run npx playwright test tests/asset-configuration.spec.ts tests/equipment-kits.spec.ts using isolated mock Vite; npm run build. Verify desktop/mobile layout. Independent review, fix findings, save evidence.

### Task4: Whole-feature review and matching local deployment
**Files:** reports .superpowers/sdd/custom-asset-configuration/; private backup folder outside repo.
- [ ] Independent whole-feature review against spec plus focused evidence; resolve Critical/Important findings before deployment. No second roadmap feature begins.
- [ ] Capture verified private DB dump, preexisting table counts/column hashes and upload hashes; freeze exact Lab API5081 only. Snapshot reviewed source. Apply010 explicitly after latest build, compare preexisting records excluding intentional type-ID/custom-field additions and new tables. Preserve request JSON and uploads byte-for-byte.
- [ ] Restart latest backend hidden using existing safe launcher; keep user Vite5174. Read-only health/config/frontend and anonymousprotectedroutes smoke; verify newmigration ledger. No real authenticated fixtures.
- [ ] Report feature1 ready at5174, test results and any limitations. Wait for user review before feature2.

## Execution
Use the previously selected subagent-driven method, sequential implementer/reviewer gates; no product edits by controller while implementers own files. Source snapshots replace Git commits. Plan self-review confirms all spec sections and five Review Focus cases map to tasks above.
