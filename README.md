# Clary by UNIO — SourceDesk

**Current Brie operation and verification (2026-10-11):** see the [Brie local runbook](docs/brie-runbook.md) for supported Inventory/Tickets actions, authenticated company boundaries, startup/test commands, deployed release and limitations. Provider/evaluation notes below include earlier checkpoints; the runbook is the current operational reference.

Local AI help desk portfolio project. React/TypeScript, ASP.NET Core/C#, PostgreSQL/pgvector, and Docker Compose. The interface follows the user's approved Clary reference.

Epson printer coverage now includes TM-T88IV, TM-T88V, TM-T20II, TM-T20III, and original TM-m30. Five searchable curated notes paraphrase official manuals and link to their sections; they are not verbatim Epson publications. Three original PDFs are saved locally under `docs/epson/originals`. Epson blocked direct downloads of the remaining references; their official URLs are retained. See [Epson coverage and evaluation](docs/epson/README.md). Full PDFs can now be uploaded and approved through Knowledge Base; the existing Epson corpus still contains the curated notes and imported USB/APD guide. Named Epson models restrict retrieved evidence before generation; unspecified models request clarification.

## Current checkpoint

Implemented: sample Markdown indexing, document title/version/approval/index status, approved vector retrieval, replaceable embedding/answer providers, OpenAI adapters, deterministic demo mode, exact-quote citation validation, inspectable passages, conservative insufficient/conflict responses, visible chat history, and database-backed conversation history with incremental diagnostic questions.

Chat saves completed user messages and answers in PostgreSQL. **Assistant** opens a fresh chat; sending its first message adds a sidebar entry immediately. Click a **Recent conversations** entry to restore and continue that chat. After refreshing, saved chats reload in the sidebar; click one to continue. Backend restarts reload their server-owned context from PostgreSQL. The former two-hour expiry, 20-message limit, and 100-session cap are removed. Chats created before persistence was added cannot be recovered if their old in-memory session was already lost. Saved history lasts while the PostgreSQL data volume is retained; it is not a backup. Saved chats are scoped to the authenticated user and current company. No automatic deletion or retention policy is implemented. Follow-ups use user-provided device details and recent messages for search. Earlier assistant advice is context, never documentary evidence. Brie displays one documented source action at a time, with conditions and warnings retained and follow-ups tracking reported outcomes. Retrieval can add up to two adjacent chunks of the highest-ranked approved document so a procedure cut at a chunk boundary is available; approval and embedding-profile filters still apply.

To understand the flow, read `backend/Clary.Api/Ai/ConversationStore.cs` (session context and clarifications), `Knowledge/ConversationRepository.cs` (saved turns), `Ai/TroubleshootingService.cs` (retrieval and grounding), and `frontend/src/App.tsx` (message history). Try: “My printer is not working” → “Not appearing in Windows Settings” → “TM-T88V” → “USB”. Confirm previous turns remain visible and each clarification uses details already supplied.

Current Brie supports permission-scoped documented answers and the Inventory/Tickets actions listed in the runbook. Documentation writes, account management, bulk actions and general record editing are not Brie capabilities. The sample conversation is not fabricated as live AI. Demo mode uses a deterministic lexical provider, not a language model.

## Upload and approve documentation

Open **Knowledge Base → Upload files**. Choose PDF, DOCX, UTF-8 TXT, or Markdown, enter a title/version, and upload as a draft. **Review extracted text** shows the full document as text. **Approve & index** enables retrieval after embeddings finish; failed indexes remain excluded. **Reindex** regenerates an uploaded document's vectors using the selected embedding profile. Each upload is a separate immutable revision, even when its title matches an earlier file; uploading a new version does not automatically revoke older versions.

Limits: 15 MB per file, 250,000 extracted characters, and 300 PDF pages. Scanned PDFs need OCR and are rejected; password-protected/malformed files are unsupported. DOCX extraction includes paragraphs and table-cell text, but not original formatting, images, or headers/footnotes. PDF extraction uses [PdfPig](https://github.com/UglyToad/PdfPig) and adds page labels; reading order for complex layouts can be imperfect, so review before approval. Original files are stored in the `clary-uploads` Docker volume; metadata, extracted text, and vectors are in `clary-data`. Locally running the API uses its `uploads` directory. Originals are preserved but a download/viewer endpoint is not implemented yet.

Known Epson model names found in an uploaded document become model-scope metadata; this identifies coverage, not proof that every procedure applies to every named model. Model assessment and exact source checks still apply. All uploaded text remains untrusted data.

The local application can give a qualified, uncited interpretation when a user reports a successful connection check and asks why it helped. This is a narrow application-selected explanation labeled **Outcome interpretation**, not a live model answer or confirmed root cause. Live synthesis may also give cautious interpretations from cited evidence and reported outcomes, while technical instructions still need documentary support.

Read `Knowledge/DocumentExtractor.cs` for extraction, `Knowledge/UploadService.cs` for storage/approval, and `Ai/CitationReferences.cs` for exact citation expansion. Ollama selects short citation references instead of copying long quotes into its output. Application code expands each reference only against that retrieved passage's existing allowed quote choices, then runs citation validation and support review. An invented ID, reference, or quote is never repaired. The generation cap remains 4,000 output tokens; incomplete outputs are withheld and reported explicitly. This avoids turning a runaway generation into an unlimited wait.

## Run locally in VS Code

Open this folder in VS Code. Install .NET 10 SDK, Node, and Docker Desktop. In this session SDK 10.0.401 was installed at `%USERPROFILE%\.dotnet`; Docker Desktop was installed and started. Keep Docker running.

1. Copy `.env.example` to `.env` and set `POSTGRES_PASSWORD` to a local password. An ignored `.env` with a generated password was created during development. Do not overwrite it casually: the existing database volume uses that password.
2. Start the backend and database in PowerShell:

```powershell
.\scripts\Start-Clary.ps1 -Provider Demo
```

3. In another VS Code terminal:

```powershell
cd frontend
npm.cmd ci
npm.cmd run dev
```

4. Open http://127.0.0.1:5174. Click **Index / reindex sample documents**. Ask `My VPN connects but I cannot access internal services.` Open its citation.

The current local HTTPS backend uses **Live Ollama** on the user's private Ubuntu server through Tailscale. Demo remains available for deterministic checks. OpenAI is optional; its earlier live request returned `credit_balance_exhausted` / `insufficient_quota`.

## Live OpenAI

Configure `OPENAI_API_KEY` as a Windows user environment variable; do not put it in React or paste it in chat. The launcher reads it into its process and passes it to the backend without printing or saving it. API billing must have usable credits. Then:

```powershell
.\scripts\Start-Clary.ps1 -Provider OpenAI
```

Reindex samples after switching providers. OpenAI defaults are `gpt-4.1-mini` for generation and `text-embedding-3-small` with 1536 dimensions. Configure model/profile values in `.env`; any embedding model/dimensions change requires reindexing. The generation model and embedding model serve different purposes.

Questions and selected passages go to OpenAI in live mode. Embedding requests send approved sample text. Generation requests use `store=false`. No model tools or external actions are enabled. If OpenAI fails, the app returns an error rather than falling back to Demo.

For backend debugging, first intentionally stop the existing local API instance to release HTTPS port 5081; keep the database running. Run `.\scripts\Start-Clary.ps1 -Provider Demo` to compile and run the local C# project. `-Compose` API startup is unsupported for the current HTTPS identity setup. Avoid dumping `docker compose config`, container environment, or env files because they may expose credentials.

## Ubuntu Ollama option (no hosted API charges)

Ollama connectivity is verified through Tailscale, and EmbeddingGemma was installed on the server through its private API. The generation model is `qwen3:8b`. These non-secret settings are saved in the ignored `.env`:

```dotenv
OLLAMA_BASE_URL=http://100.79.42.5:11434
OLLAMA_GENERATION_MODEL=qwen3:8b
OLLAMA_EMBEDDING_MODEL=embeddinggemma
OLLAMA_EMBEDDING_DIMENSIONS=768
```

Generation and embedding models are separate. The server reports Ollama 0.35.1; an embedding probe verified 768 dimensions. Its approximately 40 GB RAM and RTX 3070 with 8 GB VRAM run inference. Keep access private through Tailscale; no public exposure or server binding changes were made. The Windows browser calls the C# backend, which calls the Ubuntu server.

Once the server is reachable and models are installed, run `scripts/Start-Clary.ps1 -Provider Ollama` and reindex samples. The header must say **Live Ollama**. The provider requests structured JSON, disables thinking output, and starts with an 8192-token context limit. It rejects incomplete answers and incompatible embedding dimensions. No silent fallback to Demo or OpenAI occurs.

Configuration readiness does not establish network reachability or model quality; a real request is required. Ollama responses still pass through the same citation validation and approval rechecks. Local operation avoids API fees, but uses your server's memory, compute, and electricity.

## How the flow works

1. SampleLoader loads fictional metadata and text. Draft text is not embedded.
2. Chunker splits exact text into at most 1200 characters, overlapping by 160, preserving offsets. This is chunking: small searchable pieces retain their source provenance.
3. An embedding provider converts each passage into a vector. The profile records provider, model, dimensions, and chunking version.
4. KnowledgeRepository embeds the question with the same provider, uses pgvector cosine distance to score approved, ready, compatible passages, then ranks documents by their strongest passage. It selects up to three documents and two passages from each, with five passages total. The minimum live similarity is 0.45, and candidates must be within 0.20 of the strongest score. These settings are adjustable in `.env`; they were calibrated on a small sample set and can miss relevant evidence. Similarity is not a confidence score. Demo uses a minimum of 0.55 for its lexical fixtures.
5. TroubleshootingService sends only those passages to the answer provider. No passages means abstention. An explicit known conflict group returns both positions without proposing fixes; broader model conflict detection is not guaranteed.
6. Live responses now paraphrase and organize relevant evidence in plain language, keeping exact quotes separate. CitationValidator requires citations for every step, retrieved passage IDs, and exact quoted substrings. A second model request through ModelSupportVerifier assesses each step's support and question coverage. Every displayed step must pass; if any step fails, the draft is withheld instead of removing a possible prerequisite. A fully supported draft covering only part of the question receives an application-written limitation notice. Dynamic schemas constrain citation IDs/quotes to retrieved values and exclude obvious section labels. Approval is checked before and after the support review, and again when a passage is opened. Conflict claims require two distinct supporting quotations. Demo remains deterministic and extractive.

Dependency injection in `Program.cs` supplies `IEmbeddingProvider` and `IAnswerProvider` to the service. An Ollama adapter can implement these interfaces without changing retrieval or citation validation. `await` on HTTP and database calls lets the server wait for I/O without tying up a thread; cancellation follows the request.

Chat replies show the question or answer directly, without automatic status headings, response cards, or repeated technical footers. Clickable citations and meaningful evidence-limit/conflict messages remain. The header identifies live versus demo mode; the simpler presentation does not change backend grounding checks.

If the first search finds no eligible passages, the model reports insufficient evidence, or the support review rejects its draft, live mode tries one broader search. This removes the first pass's three-document shortlist cutoff and widens the score window, while keeping the same approval, indexing, embedding-profile, and minimum-similarity filters. It examines up to 32 candidate passages, then supplies at most eight diverse passages/9,000 characters after model and procedure filtering. It does not send the entire library to the model or admit drafts. Both attempts require the same citation validation and support review. A fallback can increase latency and still abstain. Recognized symptom wording includes green lights, doing nothing, unavailable/disabled states; already-asked clarification templates are not repeated indefinitely.

Example from our code: `passage.Text.Contains(c.Quote, StringComparison.Ordinal)` verifies an exact quote. That check proves the quote exists, not that it supports the explanation. Live paraphrases therefore also require an explicit support assessment. This assessment uses the same model in a separate request, so its errors may correlate with the draft; it is not independent proof. There is no measurable guarantee of an “85% documents / 15% model knowledge” split. Technical instructions must be grounded; the model contributes phrasing and organization. Search can miss evidence, and approval does not certify a document's technical accuracy. Untrusted documents cannot authorize tool use or override source restrictions. These controls reduce risks without eliminating them. The extra review adds model computation and response latency.

## Test

Backend unit checks:

```powershell
& "$env:USERPROFILE\.dotnet\dotnet.exe" test Clary.slnx --filter "FullyQualifiedName!~RetrievalIntegrationTests"
```

The integration test requires `CLARY_TEST_CONNECTION`, a PostgreSQL connection string to the local development database. It creates and removes a unique test schema; never point it at a production database. The test intentionally fails when this configuration is missing instead of pretending pgvector was tested. Keep credentials out of terminal output.

Browser checks require the running **Demo** API, indexed samples, and Vite server:

```powershell
cd frontend
npx.cmd playwright install chromium
npx.cmd playwright test
npm.cmd run build
```

For the current live Ollama workspace, run from `frontend`:

```powershell
$env:CLARY_LIVE_TESTS='1'
npx.cmd playwright test tests/live-workspace.spec.ts
```

Run `scripts/Evaluate-Clary.ps1` from the project root for five opt-in live evaluation cases. It validates returned citations independently and saves full responses in ignored `artifacts/live-evaluation.json`. Manually review relevance, ordering, and injection behavior; the script does not grade semantic correctness automatically.

See `docs/evaluation-phase-one.md` for executed results and limits. Unit tests use controlled HTTP responses; these verify our boundaries, not OpenAI model quality.

## Read next

- `backend/Clary.Api/Knowledge/Chunker.cs`: how passages preserve evidence.
- `backend/Clary.Api/Knowledge/KnowledgeRepository.cs`: approval/profile restrictions in SQL.
- `backend/Clary.Api/Ai/TroubleshootingService.cs`: retrieval, generation, validation.
- `backend/Clary.Api/Ai/CitationValidator.cs`: enforceable citation checks.
- `backend/Clary.Api/Ai/OpenAiAnswerProvider.cs`: structured API integration.
- `frontend/src/App.tsx`: question and source inspection flow.

Optional exercise: predict what happens when a document is revoked after retrieval but before its answer is delivered. Find the check in TroubleshootingService.

## Local scope

The local browser/API services bind to loopback. The application enforces authentication, current-company isolation and existing permissions on the server; these local tests do not certify a public production deployment. Git commits are pending because the machine has no configured author identity; no identity was invented.
