# CLAUDE.md

Guidance for Claude Code (or any agentic coding assistant) working in this
repository. Read `docs/ARCHITECTURE.md` (or `Looma-Project-Brief.md` if that
hasn't been split out yet) before making structural changes — this file is
the day-to-day ruleset; that one is the reasoning behind it.

## What this project is

Looma: a local-first, privacy-guaranteed document intelligence platform.
RAG over text, PDFs, images, and audio. Runs fully offline by default
(standalone), with a distributed/scaled mode that never compromises the
"no information leaves the system" guarantee.

## Non-negotiable architecture rules

1. **Dependency direction is one-way.** `Core` has no dependencies except
   `Microsoft.Extensions.AI.Abstractions`. `Application` depends only on
   `Core`. `Infrastructure.*` implements `Core` interfaces. `CLI` / `MCP.*`
   / future UI projects depend only on `Application` — **never** reference
   an `Infrastructure.*` project directly from a client project. If a
   client needs a capability, it goes through an `Application` interface,
   full stop.
2. **`Application.Agents` is optional and depends on `Application`, never
   the reverse.** Core RAG (index/search/answer) must build and run with
   zero knowledge that the Agents assembly exists.
3. **Long-running operations are `IAsyncEnumerable<T>`, not
   request/response with polling.** Indexing a folder, generating an
   answer — both stream. Don't add a "buffer everything then return" code
   path as a shortcut; both local and MCP-remote consumers must get real
   streaming.
4. **One `IVectorStore` implementation: Qdrant.** Don't add a SQLite or
   in-memory fallback "for simplicity" — it was tried before, caused real
   bugs (see Lessons below), and Qdrant already runs fine standalone as a
   local process. If a task seems to need a second vector store
   implementation, stop and raise it rather than adding one.
5. **Two vector collections, not one:** `documents` (text-embedding space —
   text chunks, transcripts, image captions/OCR) and `images` (CLIP space).
   Never write CLIP vectors into `documents` or vice versa — the spaces are
   not comparable and mixing them silently produces garbage search results,
   not an error.

## Lessons from a prior attempt — do not reintroduce these bugs

- **Verify NuGet package / target-framework compatibility before writing
  code against it**, especially anything in the `Microsoft.Extensions.*`
  or `Microsoft.SemanticKernel.*` families — version-to-TFM support lags
  and previously cost a lot of rework. Check before scaffolding, not after
  a failed build.
- **Never truncate document content as a stand-in for chunking.** A prior
  version truncated everything to 2000 characters "temporarily" and it
  never got fixed. Chunking-with-overlap (`RAG.ChunkSize` /
  `RAG.ChunkOverlap` in config) is required for any ingestion code path,
  no exceptions, no TODOs that defer it.
- **Persistence is not optional or an afterthought.** Don't build an
  in-memory placeholder "to get tests passing" and plan to add real storage
  later — it previously caused a class of bugs where indexing reported
  success but `count` stayed at 0. Vector storage always goes through the
  real `IVectorStore` (Qdrant), even in early scaffolding.
- **Watch JSON metadata round-tripping.** Deserializing into
  `Dictionary<string, object>` yields `JsonElement` for values, not native
  `int`/`string`/etc. — caused a runtime cast exception before. Prefer
  typed metadata DTOs over loose dictionaries where the shape is known
  ahead of time.
- **Config file discovery must be explicit, not a directory-walk hack.**
  Don't implement "search parent directories for config.json until found."
  Config path is either relative to the current working directory or
  passed explicitly via `--config` / an env var — document whichever one
  is chosen and keep it consistent across CLI, MCP.Server, and tests.
- **Don't silently downgrade or version-mismatch transitive packages** when
  a restore error appears. If package version conflicts show up, resolve
  them by aligning versions deliberately across the whole solution in one
  pass, not by bumping one project at a time.

## Security constraints (enforce in code, not just in config)

- `Infrastructure.Llm`'s HTTP clients must reject any configured endpoint
  outside `Security.AllowedInferenceHosts` in `config.json` — this check
  happens at startup/DI registration, not as documentation. A
  misconfiguration should fail loudly, not silently phone home.
- `LocalMind.MCP.Server` (now `Looma.MCP.Server`) requires auth on every
  connection and must not respond to tool-listing requests from an
  unauthenticated caller.
- Never log document content, chunk content, or file contents in error
  messages or telemetry — log identifiers/paths/counts only.

## Conventions

- Target framework: **.NET 10** across all projects.
- Chat/embedding/vision access goes through `Microsoft.Extensions.AI`
  (`IChatClient`, `IEmbeddingGenerator<string, Embedding<float>>`) —
  never call a vendor SDK (Ollama, OpenAI, etc.) directly from
  `Application` or client projects.
- Default local models and their config keys are defined in
  `config.json` under `Models.*` — see the project brief for the current
  defaults (Qwen family for chat/vision, `nomic-embed-text` for embedding,
  Whisper for STT, open_clip for image embeddings). Don't hardcode model
  names in code; always read from config.
- Prefer records/immutable types for entities in `Core` (see
  `DocumentChunk`, `ImageAsset`) — these cross process boundaries (MCP) and
  should not have mutable shared state.

## Before starting a coding session

1. Confirm which milestone from the project brief's "Suggested First
   Milestones" section is in progress — don't jump ahead to MCP/Agents
   work before the standalone text-RAG path is solid end-to-end.
2. If a task requires deviating from any rule above, say so explicitly and
   explain why, rather than quietly working around it.
3. Run/add tests for any new `Infrastructure.*` implementation against a
   real local Qdrant/Ollama instance where practical — not mocks-only —
   since the last version's most damaging bugs were integration-level
   (persistence, config discovery, path resolution), not unit-level.