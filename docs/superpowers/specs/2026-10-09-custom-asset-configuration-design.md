# Feature 1: Custom asset types, fields, and Configuration

Only feature 1 of the ten-feature inventory roadmap is in scope. Deliver and deploy it for user review, then stop. Feature 2 must wait for that review.

## User experience
Add a SlidersHorizontal Configuration icon immediately above Settings in the bottom Inventory rail. Match current toolbar, white surfaces, small outline icons, spacing and dropdowns. Configuration contains Asset types, Custom fields, Departments, People and Locations. Move directory management and assignment review here; Settings retains account, company and user administration.

Asset types start with Computer, Printer, Monitor, Network and Accessory. Administrators can create company-specific types, edit names and icons, reorder, and archive types. Stable IDs preserve existing assets and kit matching when renamed. Referenced types cannot be destroyed. Archived types remain visible on existing records but unavailable for new selections. Preserve every existing asset, category, kit and request snapshot during migration.

Use a fixed, server-validated Lucide icon palette: Box, Package, Laptop, Monitor, Printer, Server, Router, Wifi, Headphones, Keyboard, Mouse, Smartphone, Tablet, Camera, Video, Mic, Speaker, Cable, HardDrive, Cpu, Wrench, Hammer, Drill, Car, Truck, Bike, Armchair, Sofa, Lightbulb, Briefcase. No arbitrary SVG uploads or executable icon markup.

Inventory category cards and asset forms load configured types. Kits accept the same configured types. Existing documentation and Computer-specific monitoring retain their current behavior; unsupported custom types do not generate fake documentation or monitoring.

Custom fields belong to individual asset types, with optional shared fields, as selected by the user. Initial field kinds: short text, long text, number, date, yes/no, and single-choice dropdown. Settings include name, help text, required flag, order, scope and dropdown choices. Required new fields do not erase or rewrite existing values: existing records may display missing values, and the next save requires completion. Configuration edits do not bulk-modify assets. Field kind is immutable once values exist; archive and replace instead. Archived fields retain historical values without requiring them during edits.

Asset create/edit forms render applicable fields in a compact Additional details area. Asset details show the same values. When changing type, retain shared values and retain historical type-specific values without applying irrelevant required rules. Validate all values and field ownership on the backend. Custom fields cannot change protected company, role, identity, assignment or subscription fields.

## Security and data
Company-scoped type definitions, field definitions and asset values use stable IDs and composite company ownership constraints. Use an append-only migration after checking the current migration ledger; do not edit old migrations. Seed defaults per existing company and for new-company creation. Backfill stable asset and kit references from existing type codes. Request snapshots remain immutable.

Configuration mutations require company Settings permission, checked against persisted current account/session within a transaction, with optimistic revisions and audit history. Reading type/field metadata respects content visibility so assigned-only users cannot retrieve other-company data. Asset changes keep existing Write/Delete permission separation, company-first lock ordering and revision checks. Bounded labels, choices, field count and value sizes prevent unlimited metadata growth.

## Verification and release
Test migration preservation, default creation for old/new companies, tenant isolation, configuration permissions, concurrent revisions, rename/archive behavior, all six value kinds, required rules, type changes and kit stock matching for custom types. Browser tests use mocked accounts and fixtures; verify icon picker, forms, configuration navigation and desktop/mobile layouts. Run focused backend/browser tests and frontend build, obtain independent review, back up database/uploads, deploy matching frontend/backend on5174 and verify health without real fixture mutations. Tell the user feature 1 is ready and wait for review before feature 2.

## Remaining implementation choices
Select physical schema and compatibility DTO strategy during implementation planning. Preserve legacy display fields where existing integrations depend on them. Validate palette identifiers, never accept arbitrary icon names. No unrelated storage, billing, driver or Reports changes in this feature.
