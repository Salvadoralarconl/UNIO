# Clary Lab — experimental working copy

Created October 7, 2026 from C:\Projects\Apps\SourceDesk.

The original folder and its Docker services remain the backup. All future experiments belong in C:\Projects\Apps\Clary-Lab.

Planned authenticated Lab frontend: http://localhost:5174
Planned authenticated Lab API: https://localhost:5081
Lab PostgreSQL port: 5433
Compose project: clary-lab

The database (including conversations/indexes) and uploaded files were copied into separate Docker volumes. The private Ollama endpoint is shared; changing the lab's documents or conversations does not change the original database. Backend-only environment configuration was copied without displaying secrets.

Run backend after the review/configuration/repair gates: ./scripts/Start-Clary.ps1 -Mode Normal -Provider Ollama
Run frontend from frontend directory: npm ci, then npm run dev

The actual old API remains running pending staged activation. See [company foundation operations](docs/company-foundation-operations.md) for explicit fresh bootstrap/migrate/normal modes, private Local identity setup, sole-Operator binding, preservation rehearsal, the one dangling legacy replacement-link repair proposal and rollback. Never rerun legacy001 initialization on migrated data. Stage1 prepares the reviewed Local login rollout; actual repair/migration/restart/owner binding await final security review and a fresh paired backup. The exact legacy repair and matching deployment are already user-approved. The user chooses credentials privately after offline setup-link issuance.

The ignored artifacts/clary-lab-copy.dump file is the database snapshot used to seed the lab. It contains private application data; do not publish it.
## Approved suite layout — October 7, 2026

The lab uses the approved Nodo-inspired layout: small module tabs, a module-specific icon rail, a dominant chat panel and an independent Sources panel. Clary's rail starts a new chat, opens previous chats, or toggles Sources. Documentation retains upload, extracted-text review and approval/indexing. Tickets and Inventory are navigation placeholders and have no working data features yet.

Module selection lives separately from chat state, so switching tabs preserves the active conversation and typed draft. Previous chats load through the existing backend API; citations retain their exact-passage inspection flow. No backend or original SourceDesk files changed for this layout.

Read frontend/src/App.tsx for module and drawer state, frontend/src/suite.css for layout dimensions, and frontend/tests/suite-navigation.spec.ts for the navigation regression. Run npm run build and npx playwright test tests/suite-navigation.spec.ts from frontend. Opt-in live chat checks: set CLARY_LIVE_TEST=1 and run tests/conversation.spec.ts and tests/live-workspace.spec.ts.

Verification: production build passed; five browser checks passed covering retained/restored chat, live printer/VPN citations, unsupported/conflicting questions and module navigation. The final navigation recheck also passed keyboard focus containment/Escape, upload dialog access, Sources toggle, draft preservation and a 390px mobile viewport. Tickets and Inventory remain placeholders.

## Documentation phase one — October 7, 2026

Documentation now uses the approved wiki library layout in the existing suite shell, with the same outer height, margins and typography scale as Clary. Create nested folders with New folder, open a file in the center reader, and use Move to to organize existing files. Breadcrumbs navigate back up the hierarchy. Title search searches the entire document list. Review queue shows unapproved or unready documents. Upload files still creates a draft; Approve & index makes it searchable after successful indexing.

Folders and document placements persist in PostgreSQL (`library_folders`, `document_folders`). A document remains the same document when moved: canonical text, revision ID, approval and embeddings are untouched. No existing documents are automatically moved. New uploads start at Library root. Folder renaming/deletion/reparenting, full-text library search, original-file preview/download, attachments between pages and wiki page creation/editing are not implemented in this phase. The New page control is explicitly disabled until editing is implemented.

The reader displays full extracted text with basic Markdown headings and bullet styling. It renders untrusted text as React text nodes; it does not execute uploaded HTML or scripts. Tables, images, inline Markdown and original PDF layout are not faithfully reproduced. On this page links are available when the extracted text contains Markdown headings. Clary's existing approved-document search and citation checks remain in force; folders do not restrict search.

Read `frontend/src/Documentation.tsx` for browser state and asynchronous load guards; `frontend/src/documentation.css` for the approved layout; `backend/Clary.Api/Knowledge/LibraryRepository.cs` for persistence; `database/001-initial.sql` for the additional tables. Endpoints: GET /api/library, POST /api/library/folders, POST /api/documents/{id}/folder. The injected repository receives the shared database connection pool from ASP.NET dependency injection, keeping SQL out of UI code.

Verification: 87 backend tests passed with zero skips/failures, including isolated-schema folder persistence, invalid-parent rejection and unchanged approval/text after moving. Production frontend build passed. Browser checks passed for nested-folder browsing, safe text rendering, title search, folder creation/reload, delayed-move navigation, mobile overflow, suite navigation and a real uploaded draft through review/approval/indexing. The recorded synthetic upload was removed from the lab database after testing. The original SourceDesk backup was not changed.

Exercise: move a manual into a folder, then refresh. Predict whether Clary can still retrieve that manual. The answer should be yes if it is approved, indexed and relevant; folder placement is only navigation metadata.
