You are assisting with the Looma project. Follow these rules:

1. Respect the architecture: Core → Application → Infrastructure. Never introduce cross-layer dependencies.
2. All inference is local-first. Never suggest cloud APIs or external endpoints.
3. Use streaming-first patterns (`IAsyncEnumerable<T>`).
4. Use the abstractions defined in Looma.Core (`IVectorStore`, IAudioTranscriber, IImageCaptioner, etc.).
5. Infrastructure implementations must remain vendor-agnostic behind interfaces.
6. Never truncate documents; always use chunking-with-overlap.
7. All RAG operations must be deterministic and reproducible.
8. When generating code, follow .NET conventions: async-first, DI-friendly, clean architecture.
9. When unsure, ask for clarification instead of guessing.
10. Keep responses concise, structured, and professional.

# Looma — Project Brief

A local-first, privacy-guaranteed document intelligence platform: RAG over text,
PDFs, images, and audio, running entirely on-device by default, with an
explicit path to distributed/scaled deployment that never compromises the
"no information leaves the system" guarantee.

This document is the starting reference for the project — architecture,
decisions made, defaults chosen, and open questions still to resolve.

---

## 1. Core Principles

1. **Standalone-first, distributed-capable.** Runs fully offline on one
   machine. Scaling to multiple users/machines happens by swapping DI
   registrations behind stable interfaces — client code never changes.
2. **No information leaves the system.** Every model (chat, embedding,
   vision, speech) runs locally. Outbound network access from inference
   clients is allowlisted to local/private addresses only, enforced in code
   — not just by configuration convention.
3. **One set of interfaces, many backends.** `Core` defines contracts
   (`IVectorStore`, `IAudioTranscriber`, `IImageCaptioner`,
   `IImageEmbeddingGenerator`) plus consumes `Microsoft.Extensions.AI`'s
   `IChatClient` / `IEmbeddingGenerator<TInput,TEmbedding>`. Nothing outside
   `Infrastructure.*` depends on a concrete vendor SDK.
4. **Streaming is first-class, not degraded.** Long-running operations
   (indexing a folder, generating an answer) are `IAsyncEnumerable<T>` from
   the start. Local mode and MCP-remote mode both stream natively — no
   buffer-and-return fallback anywhere.

---

## 2. Solution Structure (Clean Architecture)

```
Looma.Core
    Entities + abstractions. Zero external dependencies beyond
    Microsoft.Extensions.AI.Abstractions.

Looma.Application
    RAG use cases: index, search, answer. Streaming-first. Transport- and
    backend-agnostic — knows nothing about Qdrant, Ollama, or MCP.

Looma.Application.Agents          [OPTIONAL]
    Agent/workflow orchestration (e.g. customer-support, order-tracker style
    flows). Depends on Application, never the reverse. Only loaded if
    config.json enables it — core RAG has zero dependency on this assembly.

Looma.Infrastructure.VectorStore.Qdrant
    The one and only IVectorStore implementation. Collection-aware:
    "documents" (text-space) and "images" (CLIP-space) as separate
    collections. Qdrant runs as a lightweight local process in standalone
    mode and as a cluster in scaled mode — same code either way.

Looma.Infrastructure.Llm
    IChatClient, IEmbeddingGenerator<string,Embedding<float>>,
    IAudioTranscriber, IImageCaptioner, IImageEmbeddingGenerator
    implementations. Talks to local Ollama (chat/embedding/vision) and local
    ONNX runtimes (Whisper for speech, CLIP for image embeddings) over
    OpenAI-compatible HTTP or in-process ONNX Runtime.

Looma.Infrastructure.Redis        [scaled mode only]
    Indexing job queue, progress pub/sub fan-out, embedding-result cache.
    Absent entirely in standalone mode — indexing runs synchronously,
    in-process, no network hop.

Looma.MCP.Server
    Auth-gated MCP server exposing Application as streaming tools
    (index_directory, answer_question, etc. — explicit streaming tool
    schemas, not generic passthroughs). Not a public/discoverable surface;
    locked down (see Security below).

Looma.MCP.Client
    Typed client. Implements the same Application interfaces over MCP, so
    consuming code is identical to the local-DI path.

Looma.CLI
    Depends only on Application interfaces. Standalone (local DI) or
    MCP-client mode, chosen via config.json Deployment.Mode.

Looma.UI.MAUI.Desktop             [not yet scaffolded]
    Same pattern as CLI: standalone or MCP-client mode.

Looma.UI.MAUI.Mobile              [not yet scaffolded]
    MCP-client only, always. Mobile can't run Qdrant/Ollama in-sandbox, so
    it's never "standalone" — always talks to a server (LAN or remote).
```

---

## 3. Deployment Shapes

| Shape | Vector store | LLM | Transport |
|---|---|---|---|
| Solo desktop | Local Qdrant process | Local Ollama | In-process DI |
| Home server | Qdrant on the server | Ollama on the server | MCP over LAN, TLS + API key |
| Scaled / multi-user | Qdrant cluster | Ollama/vLLM cluster | MCP.Server + Redis (queue, pub/sub, cache) |

Same `Application` interfaces in every shape. Only `Program.cs` DI wiring
changes between them.

---

## 4. Vector Storage Design

`IVectorStore` is collection-aware from the start, not a single flat store:

- **`documents`** — text chunks, audio transcripts, image captions/OCR. All
  embedded with the same text embedding model, so every media type is one
  searchable space.
- **`images`** — CLIP vectors, a separate space. Enables both image→image
  similarity search and text→image search (via CLIP's joint text/image
  embedding space).

---

## 5. Ingestion Pipeline by Media Type

| Input | Pipeline |
|---|---|
| Text / PDF / docx / md / csv | chunk (with overlap, real line-range tracking) → embed (text) → `documents` |
| Audio | transcribe (Whisper, local) → chunk (with timestamp ranges) → embed (text) → `documents` |
| Image | **both, in parallel:** (a) caption + OCR (vision-language model) → chunk → embed (text) → `documents`; **and** (b) CLIP-embed → `images` |

Chunking is real chunking with overlap — not truncation. This was a known
flaw in the prior implementation (documents were truncated at 2000 chars)
and is explicitly designed around this time.

---

## 6. Default Local Models

Never bundled in an installer — auto-pulled on first run via a `looma
setup` step (through Ollama for chat/embedding/vision, direct ONNX fetch for
Whisper/CLIP). Fully offline after that one-time pull.

| Role | Default model | License | Notes |
|---|---|---|---|
| Chat / generation | Qwen 3 4B (8GB tier) / Qwen 3 8B (16GB, recommended default) / Qwen 3.6 27B (24GB+) | Apache 2.0 | Tiered by hardware; native tool-calling on larger sizes for the optional Agents layer |
| Embedding | `nomic-embed-text` | Apache 2.0 | Purpose-built for RAG, 137M params |
| Vision (caption + OCR) | Qwen2.5-VL (small) | Apache 2.0 | Same family as chat model — one vendor's license to reason about |
| Image embeddings | open_clip ViT-B/32 | MIT | Standard, well-tested, genuinely open |
| Speech-to-text | Whisper (base/small) | MIT | Local via Whisper.net, ONNX-based |

**Why mostly Qwen:** Apache 2.0 is unambiguous for redistribution/commercial
use, and the family spans every hardware tier plus a vision variant — fewer
licenses to review than mixing families. Llama/Gemma remain
user-selectable alternatives in config, not defaults, due to additional
usage-restriction terms in their licenses.

**Embedding model is not casually swappable.** Changing it requires
re-indexing every collection (incompatible vector spaces). Every other
model (chat, vision, STT) is swappable via config with zero data migration.

---

## 7. Security Posture

- **MCP server:** every connection authenticated; no tool listing without
  auth; TLS always, even on private networks.
- **Redis/Qdrant:** network-isolated, never publicly exposed, auth enabled
  on both (Redis `requirepass`/ACLs, Qdrant API-key auth).
- **Inference clients enforce locality in code:** `Infrastructure.Llm`'s
  HTTP clients reject any configured endpoint that isn't
  localhost/private-range/explicitly allowlisted. A misconfiguration (e.g.
  someone sets a public API endpoint) cannot silently leak data — it fails
  at startup instead.
- **More models without leaving "local-first":** since the LLM layer is an
  OpenAI-compatible HTTP client, any local inference engine (Ollama,
  llama.cpp server, vLLM, LocalAI, text-generation-webui) is a config
  change, not new code — unlocking the full open-weight model ecosystem
  while keeping the network-layer guarantee intact. Self-hosting bigger
  models on a private server the user controls is still "no data leaves" —
  it's infrastructure they own, not a public API.

---

## 8. Reference `config.json`

```json
{
  "DomainName": "Looma",
  "Version": "2.0.0",
  "Deployment": {
    "Mode": "Standalone",
    "McpServerEndpoint": null
  },
  "Models": {
    "BaseModel": {
      "Provider": "Ollama",
      "Endpoint": "http://localhost:11434",
      "Model": "qwen3:8b",
      "ContextSize": 8192
    },
    "EmbeddingModel": {
      "Provider": "Ollama",
      "Endpoint": "http://localhost:11434",
      "Model": "nomic-embed-text",
      "Dimensions": 768
    },
    "VisionModel": {
      "Provider": "Ollama",
      "Endpoint": "http://localhost:11434",
      "Model": "qwen2.5vl:7b"
    },
    "ImageEmbeddingModel": {
      "Provider": "Local.OnnxClip",
      "ModelPath": "./models/clip-vit-b32.onnx",
      "Dimensions": 512
    },
    "SpeechToTextModel": {
      "Provider": "Local.Whisper",
      "ModelPath": "./models/whisper-base.bin"
    }
  },
  "VectorStore": {
    "Provider": "Qdrant",
    "Endpoint": "http://localhost:6333",
    "ApiKey": null,
    "Collections": {
      "Documents": "documents",
      "Images": "images"
    }
  },
  "Redis": {
    "Enabled": false,
    "Endpoint": "localhost:6379",
    "Password": null
  },
  "Mcp": {
    "Enabled": false,
    "Auth": {
      "Mode": "ApiKey",
      "ApiKeyEnvVar": "LOOMA_MCP_API_KEY"
    }
  },
  "RAG": {
    "Sources": [
      {
        "Type": "FileSystem",
        "Path": "./data",
        "FileTypes": [".pdf", ".docx", ".xlsx", ".txt", ".md", ".csv", ".png", ".jpg", ".jpeg", ".wav", ".mp3"],
        "Recursive": true
      }
    ],
    "ChunkSize": 400,
    "ChunkOverlap": 50,
    "TopK": 5,
    "MinRelevanceScore": 0.7
  },
  "Security": {
    "AllowedInferenceHosts": ["localhost", "127.0.0.1", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"],
    "BlockNonLocalEndpoints": true
  },
  "Agents": {
    "Enabled": false,
    "Definitions": []
  }
}
```

---

## 9. Lessons Carried Over From the Prior Attempt

These are concrete, hard-won fixes baked into the decisions above — worth
keeping visible so they aren't accidentally reintroduced:

- **Check package/.NET version compatibility before committing to a target
  framework.** `Microsoft.Extensions.VectorData.Abstractions` (net8.0-only)
  cost significant time last attempt. Resolved this time by using Qdrant
  over HTTP instead of relying on that package at all.
- **Chunk documents properly; never truncate.** Prior implementation
  truncated at 2000 characters as a stopgap — this time chunking-with-overlap
  is a first-class part of the ingestion pipeline design, not an
  afterthought.
- **Design persistence in from day one.** Prior implementation started with
  an in-memory vector store and bolted on SQLite persistence later, causing
  a "documents indexed but count is 0" class of bugs. This time the vector
  store is a real external service (Qdrant) from the first line of code.
- **Don't let JSON metadata round-tripping surprise you.** Deserializing
  `Dictionary<string, object>` from JSON yields `JsonElement`, not native
  types — caused a casting bug before. Worth deciding metadata shape/typed
  DTOs deliberately this time rather than loose dictionaries.
- **Config discovery should be intentional, not a directory-walk hack.**
  Prior CLI walked up parent directories looking for `config.json`. This
  time, config location should be an explicit, documented convention
  (e.g. always relative to working directory, with a `--config` override).

---

## 10. Open Questions (resolve before/while scaffolding)

- **MCP auth mechanism:** API key, mTLS, or both (mTLS for server-to-server,
  API key for CLI/mobile clients)?
- **Exact chunking parameters:** proposed default ~400 tokens / 50 overlap —
  confirm or tune.
- **Model provisioning mechanism:** does `looma setup` shell out to
  `ollama pull`, or manage its own model cache directory uniformly across
  Ollama and non-Ollama models (Whisper, CLIP)?
- **Redis backpressure/eviction policy** for the indexing job queue at
  scale — not yet designed.
- **MAUI Desktop/Mobile** — architecture decided (standalone-or-MCP for
  desktop, MCP-only for mobile) but not yet scaffolded.

---

## 11. Suggested First Milestones

1. Scaffold `Core` + `Application` interfaces (streaming use cases,
   `IVectorStore`, media-specific abstractions) — no implementations yet.
2. Stand up `Infrastructure.VectorStore.Qdrant` against a local Qdrant
   instance; validate `documents` + `images` collections end-to-end.
3. Stand up `Infrastructure.Llm` against local Ollama (chat + embedding
   only first); wire the network-locality enforcement early, not last.
4. CLI: `index`, `answer` (streaming), `count` — standalone mode only.
5. Add audio/image ingestion (Whisper, vision captioning, CLIP) once the
   text path is solid end-to-end.
6. MCP server + client, mirroring the CLI's use cases, with auth from the
   first commit.
