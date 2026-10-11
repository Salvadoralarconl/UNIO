# SourceDesk: design and first-phase scope

## Purpose

A local portfolio project demonstrating practical AI integration and IT support experience. Success means a user can inspect the documentation behind troubleshooting guidance, see when evidence is insufficient or conflicting, record attempted fixes, and prepare an escalation ticket. Learning and genuine functionality take priority over building every feature at once.

## Stack and location

Project root: `C:\Projects\Apps\SourceDesk`.

React with TypeScript and Vite provides the interface. ASP.NET Core with C# owns ingestion, retrieval, conversation orchestration, validation, and exports. PostgreSQL with pgvector stores document metadata, passages, embeddings, and support sessions. Docker Compose runs local services. Target .NET 10; confirm package compatibility during implementation.

The machine currently has Node, npm, Git, VS Code, and a .NET runtime, but no .NET SDK or Docker. Install the SDK and Docker Desktop before full integration verification. Do not replace PostgreSQL with SQLite or replace C# with JavaScript to avoid these prerequisites. A simpler stack reduces setup but would miss the requested learning goals. An OpenAI-managed retrieval store reduces database work but would reduce control over approval filtering and index provenance. Local pgvector is the chosen approach.

## Responsibilities and boundaries

The frontend calls only the local backend. API credentials remain on the backend. The user authorized reusing OPENAI_API_KEY from the Windows user environment. A launcher may copy it into its own process environment, without printing it or persisting it. Compose receives the process variable. A missing credential produces an explicit configuration error, never a silent simulated answer.

Separate interfaces for text generation and embeddings allow an OpenAI implementation now and an Ollama implementation later. Register these interfaces through dependency injection: services receive their dependencies rather than constructing a particular provider internally. Use asynchronous network and database operations with cancellation, timeouts, bounded retries, and safe errors.

2026-10-06 provider update: OpenAI returned exhausted-credit errors. The user identified an existing Ubuntu Ollama server with `qwen3:8b` (5.2 GB). Bring forward the Ollama adapters for a no-API-fee path. Server address and embedding model availability are pending. Keep OpenAI selectable, preserve the approved UI and application trust controls, and reindex when switching embedding profiles. Performance depends on the Ubuntu server hardware, not the Windows client hardware.

Confirmed later in this phase: private Tailscale endpoint `http://100.79.42.5:11434`, Ollama 0.35.1, approximately 40 GB server RAM, RTX 3070 with 8 GB VRAM. Installed and verified EmbeddingGemma with 768 dimensions. Do not expose Ollama publicly.

OpenAI receives the question, relevant session context, and retrieved passages, not unrestricted access to the knowledge base. No fine-tuning, web search, shell execution, or external action tools in version one. Selected passages and questions leave the machine for the hosted API; disclose this in setup and the UI.

## Document and index lifecycle

Documents have immutable revision IDs, title, version, approval status, indexing status, original filename, content hash, and extraction provenance. Approval states are draft, approved, and revoked. Indexing states are pending, indexing, ready, and failed. New uploads start as drafts. Replacements create new revisions; superseded versions are not implicitly trusted.

Split extracted text into bounded, overlapping passages while preserving source locations. Store extracted canonical text, passage IDs, character offsets, and PDF page or DOCX paragraph references where available. Cite extracted passages and make originals accessible for comparison. PDF support initially covers text PDFs; image-only PDFs return a clear OCR-not-supported error.

Store embedding provider, model, dimensions, and chunking version for each index. Use the same embedding configuration for documents and queries. Reject incompatible indexes and require reindexing after embedding configuration changes. Publish a new index only after all its passages succeed. Search joins metadata and restricts to approved, ready, compatible document revisions. Recheck approval before delivering answers or serving citation passages.

## Troubleshooting and trust controls

RAG means retrieving passages before generation. An embedding is a numeric representation of text; pgvector compares query and passage vectors to find similar content. Similarity is a retrieval signal, not proof that a passage answers a question. Tune relevance thresholds against evaluation examples, and use conservative abstention when evidence is inadequate.

Request structured answers with status, clarifying questions, proposed steps, and claim-level citations containing passage IDs and exact quotes. Treat uploaded text as untrusted evidence, never instructions. Keep it separated from trusted instructions. The model has no capabilities to follow document requests to read secrets or execute actions.

Application code checks response shape, allowed passage IDs, exact quote membership, current approval, and citations on actionable guidance. It constructs citation links itself; never trust model-supplied paths or URLs. Reject invalid output and return an explicit validation failure instead of displaying unvalidated advice. Questions and abstentions need no supporting fix citation. Exact quote matching does not prove semantic entailment. Document injection can still influence generated text; prompts, structural checks, and evaluations reduce risk but cannot eliminate hallucination or injection.

Phase-one refinement after real-model evaluation: use extractive step wording, requiring each step text to equal its cited source quote. Response schemas provide actual passage/quote choices. This prevents a real but unrelated quote from being attached to invented instructions, at the cost of less natural phrasing. It does not guarantee relevance or procedure completeness. The user's requested two-stage retrieval ranks relevant documents first and narrows passages inside those documents; use adjustable defaults of three documents, two passages per document, five total passages, minimum similarity 0.45 and a 0.20 relative score window. Test that long documents cannot monopolize context.

Identify conflicting evidence and show both supporting passages without choosing a procedure arbitrarily. Add a structured conflict fixture in phase one; evaluate broader model conflict detection in phase two. Do not advertise universal conflict detection.

Track device details and attempted fixes with user-confirmed results. Proposed steps are not recorded as attempted until the user confirms them. Generate escalation summaries from stored session facts and validated sources; do not invent ticket details. Users review, copy, or export locally. No external ticket submission.

## Interface: sole approved visual reference

The user supplied and explicitly approved `C:\Users\sapro\Downloads\Pastel Clary stars on a beige interface.png`. Follow this reference faithfully; it supersedes the earlier generic styling direction. SourceDesk remains the temporary folder/project name; the visible product branding is Clary by UNIO, as shown in the reference. No alternate design is approved.

Match the warm beige background, ivory rounded panels, black headings, muted taupe secondary text, restrained green approval pills, pastel star branding, dark primary action buttons, outline icons, spacing, and visual hierarchy. At the reference desktop size use the same three-column composition: left navigation and recent conversations; central troubleshooting header, conversation, numbered guidance with inline citation buttons, resolution controls, and bottom composer; right Sources/Escalation tabs, source cards with exact quote blocks and Open document actions, and attempted-fixes panel with Prepare escalation action. Preserve the displayed navigation labels Assistant, Knowledge Base, Tickets, and Settings. Implement responsive adaptation while preserving the reference hierarchy.

Use this layout from phase one, adding functional controls as their phases deliver them. Do not populate fake recent conversations or pretend that unimplemented actions work. Reference text and checked fixes are visual examples, not live evidence or confirmed attempts. Demo conversation content may appear only in explicitly labeled demo mode. Live steps and quote blocks come from validated responses and actual indexed passages. Show document approval and indexing independently in the knowledge-base view. Keep a persistent, accurate origin label using the reference's workspace badge position: Live OpenAI or Demo workspace. Sample documents used with OpenAI still produce live AI responses and must be labeled accordingly. Source actions open inspectable passages with provenance and access to the original document.

## Incremental delivery

### Phase one: first end-to-end slice

Create the repository, solution, frontend, database schema, and Compose configuration. Seed realistic fictional Markdown documents about VPN connectivity and printer queues, with approved and draft examples and an explicit conflict example. Implement ingestion of the seeded text, chunking, OpenAI embeddings, approval-filtered vector retrieval, structured OpenAI generation, citation validation, and passage inspection in the React workspace. Include a clearly labeled deterministic provider for repeatable tests, with no silent fallback from live mode. Produce setup instructions, a secret-free .env.example, and a phase-one README.

Acceptance: a supported sample question returns inspectable validated quotes; a draft document never enters model context; an unsupported question abstains; a conflict fixture shows both sources; a forged passage ID or fabricated quote fails validation; an injected document cannot expand source access or invoke actions. Run real database integration tests and at least one live supported/unsupported API check when prerequisites and credentials permit. Report unavailable checks as unrun.

### Phase two: knowledge-base management

Add PDF, DOCX, TXT, and Markdown uploads; format-aware extraction; upload size/type checks; metadata and approval controls; indexing progress and failure recovery; index rebuilds. Expand conflict, relevance, prompt-injection, and approval-revocation evaluations.

### Phase three: support workflow and portfolio finish

Persist conversation context, clarifying questions, device details, attempted fixes, and results. Add reviewable escalation copy/export and finish accessibility and responsive layout. Document architecture, measured evaluation results, tradeoffs, and limitations.

## Evaluation and teaching

Use deterministic tests to verify application enforcement and separate opt-in live evaluations for model behavior. Cover approved retrieval, unsupported questions, conflicting sources, invalid citations, and document prompt injection. Record configuration, cases, actual outcomes, and which checks used simulated responses; no claimed results before execution. Live evaluations are qualitative observations rather than guarantees.

Before each phase explain its purpose and connection to the whole app. After each phase explain how to run it, what was tested, and the code to read. Use brief examples from the actual implementation to teach chunking, vector search, API calls, dependency injection, asynchronous work, and citation checks. Offer optional exercises without blocking progress.

## Out of scope for version one

Fine-tuning, production hosting, multiple-user authentication, automatic device remediation, OCR, autonomous ticket submission, and claims of guaranteed factual accuracy. Bind local services to loopback where possible. Approval controls are local workflow controls rather than a production authorization system.
