# config.json reference

Every top-level section in `config.json`, field by field, reflecting what
the code actually reads today — not the original brief's static example
(`docs/looma-project-brief.md` section 8), which has drifted as fields were
added. Where a field isn't actually consumed by anything yet, that's called
out explicitly rather than left implied.

Both `Looma.CLI` and `Looma.MCP.Server` read the same `config.json` shape.
Discovery is simple and explicit for both: `config.json` in the current
working directory, or an explicit path via `--config <path>`. Neither walks
up parent directories looking for it.

## `DomainName`, `Version`

```json
"DomainName": "Looma",
"Version": "2.0.0"
```

Identification only — not read by any startup logic today.

## `Deployment`

```json
"Deployment": {
  "Mode": "Standalone",
  "McpServerEndpoint": null
}
```

| Field | Meaning |
| --- | --- |
| `Mode` | `"Standalone"` (default — Looma.CLI talks to Qdrant/Ollama directly, in-process) or `"McpClient"` (Looma.CLI talks to a remote `Looma.MCP.Server` instead, via `Looma.MCP.Client`). Anything else fails at startup with a clear error. |
| `McpServerEndpoint` | Required when `Mode` is `"McpClient"` — the server's base URL, e.g. `"http://localhost:3001"`. Ignored in `"Standalone"` mode. |

Only `Looma.CLI` reads this section. `Looma.MCP.Server` doesn't — it's
always the server, never a client of another server. See
`docs/mcp-server.md` for the full McpClient-mode walkthrough.

## `Models`

Fully covered in `docs/model-setup.md`. Summary: `BaseModel` and
`EmbeddingModel` are fatal to misconfigure (the CLI/server refuse to start);
`VisionModel`, `ImageEmbeddingModel`, and `SpeechToTextModel` are
best-effort — only the media type that needs them fails if provisioning
does. Every `Models.*` entry shares this shape:

| Field | Meaning |
| --- | --- |
| `Provider` | `"Ollama"` for chat/embedding/vision models, `"Local.OnnxClip"` for `ImageEmbeddingModel`, `"Local.Whisper"` for `SpeechToTextModel`. |
| `Endpoint` | Ollama-only. Must pass the `Security.AllowedInferenceHosts` check (see below) or DI registration fails outright. |
| `Model` | Ollama-only — the model tag to pull/use (e.g. `"qwen3:8b"`). |
| `ContextSize` | Ollama-only, optional. |
| `Dimensions` | Embedding vector size — `768` for `EmbeddingModel` (nomic-embed-text), `512` for `ImageEmbeddingModel` (CLIP ViT-B/32). Used to size the Qdrant collection. |
| `DisableThinking` | `BaseModel` only. `true` by default — suppresses Qwen3's hidden chain-of-thought pass, since `answer` is grounded Q&A, not open-ended reasoning. |
| `ModelPath` | `Local.*` providers only — where the model file lives on disk (relative paths resolve against the working directory). |
| `DownloadUrl` | `Local.*` providers only — direct HTTP source `LocalModelFileProvisioner` fetches `ModelPath` from on first run if it's missing. Best-effort, not fatal. |
| `TimeoutSeconds` | HTTP timeout for calls to this model. Defaults to `600` — the OpenAI SDK's own 100s default is sized for a cloud API, not a local model that might still be loading or running on CPU; a real captioning call hit exactly this. |

## `VectorStore`

```json
"VectorStore": {
  "Provider": "Qdrant",
  "Endpoint": "http://localhost:6333",
  "ApiKey": null,
  "Collections": { "Documents": "documents", "Images": "images" }
}
```

| Field | Meaning |
| --- | --- |
| `Endpoint` | Qdrant's HTTP endpoint. |
| `ApiKey` | Qdrant API-key auth. `null` is only acceptable for a local, network-isolated instance. |
| `Collections.Documents` / `.Images` | Collection names for the two vector spaces — text-embedding space and CLIP space. Never mixed; see CLAUDE.md rule 5. |

`Provider` is effectively fixed at `"Qdrant"` — CLAUDE.md rule 4 is explicit
that a second `IVectorStore` implementation is a deliberate, discussed
decision, not something to add quietly.

## `Redis`

```json
"Redis": { "Enabled": false, "Endpoint": "localhost:6379", "Password": null }
```

**Entirely inert.** Nothing in the codebase reads this section yet — it's
reserved for the brief's "Scaled / multi-user" deployment shape (queue,
pub/sub, cache), not implemented in any milestone so far.

## `Mcp`

```json
"Mcp": {
  "Enabled": false,
  "Auth": { "Mode": "ApiKey", "ApiKeyEnvVar": "LOOMA_MCP_API_KEY" },
  "AllowedHosts": ["localhost", "127.0.0.1", "::1"]
}
```

Full detail in `docs/mcp-server.md`. Summary:

| Field | Meaning |
| --- | --- |
| `Enabled` | **Inert** — not read by `Looma.MCP.Server` (which starts whenever you run it, regardless of this flag) or `Looma.CLI` (mode selection is entirely `Deployment.Mode`, not this flag). Kept for shape-compatibility with the brief; may get wired to something later. |
| `Auth.Mode` | Must be `"ApiKey"` — the only mode `Looma.MCP.Server` and `Looma.MCP.Client` support so far. Anything else fails at startup on both sides. |
| `Auth.ApiKeyEnvVar` | Name of the environment variable holding the shared API key (not the key itself — never put a real key in config.json). Both server and client read the *same* env var name from their own process's environment; the actual key value must match between the two processes. |
| `AllowedHosts` | Host-header allow-list the server checks on every request (DNS-rebinding defense). Defaults to loopback-only if omitted. |

## `RAG`

```json
"RAG": {
  "Sources": [{ "Type": "FileSystem", "Path": "./data", "FileTypes": [...], "Recursive": true }],
  "ChunkSize": 800,
  "ChunkOverlap": 100,
  "TopK": 5,
  "MinRelevanceScore": 0.55,
  "MaxAnswerTokens": null,
  "AnswerTemperature": 0.1
}
```

**`Sources` is mostly vestigial.** Only `Sources[0].Path` is actually read
— as the default folder for `looma index` when no path argument is given.
`Type`, `FileTypes`, and `Recursive` inside it are **not consumed by
anything**: supported file types are hardcoded per media type
(`DocumentTextExtractor`/`ImageFile`/`AudioFile`'s own `SupportedExtensions`
lists), and recursion is controlled by the CLI's `--no-recursive` flag, not
this config. Don't rely on editing `FileTypes` here to change what gets
indexed — it won't.

| Field | Meaning |
| --- | --- |
| `ChunkSize` / `ChunkOverlap` | Characters (not tokens — no tokenizer is wired up yet), treated as an upper bound on whole lines/segments packed together, never a raw character-slice window. Applies uniformly to text, image captions, and audio transcripts. Raised from 400/50 to 800/100 — see `RagOptions.cs`'s doc comment for the reasoning (both models' real context budgets have room for bigger, more complete chunks). Changing this only affects newly indexed content — re-index (`looma index --clear`) to apply a new value to files already in the collection. |
| `TopK` | Default number of results `search`/`answer` retrieve. |
| `MinRelevanceScore` | Cosine similarity threshold a chunk must clear to be used as `answer` context. Calibrated against real nomic-embed-text scores (0.55) — see `RagOptions.cs`'s doc comment before changing this; use `looma search "<query>" --min-score 0` to see real scores first, don't guess. |
| `MaxAnswerTokens` | Optional hard cap on generation length. `null` by default — a safety net against runaway generation, not a speed optimization; leave it unset unless you've actually hit that. |
| `AnswerTemperature` | Chat sampling temperature for `answer`. Low (`0.1`) by default, deliberately — grounded Q&A should stick to the provided context, not free-associate. |

## `AnswerCache`

```json
"AnswerCache": {
  "Enabled": true,
  "FilePath": "./.looma/answer-cache.json",
  "CollectionName": "answer_cache",
  "SemanticSimilarityThreshold": 0.97
}
```

| Field | Meaning |
| --- | --- |
| `Enabled` | Toggles caching. Reuses `VectorStore.Endpoint`/`ApiKey` (same Qdrant instance) — this section only adds cache-specific settings. |
| `FilePath` | Exact-match layer: a local JSON file keyed by normalized question text. Resolved relative to the working directory. |
| `CollectionName` | Semantic-fallback layer: a dedicated Qdrant collection for cached question embeddings — deliberately separate from `documents`/`images` (never mix embedding purposes in one collection). |
| `SemanticSimilarityThreshold` | Cosine similarity a candidate question must clear to count as "the same question." Deliberately strict (`0.97`) — a false positive here means confidently serving a wrong answer, worse than the latency the cache exists to avoid. |

Run `looma clear-cache` after a prompt/model/config change that a re-index
wouldn't otherwise invalidate — the cache's staleness check only tracks
re-indexing, not generation-affecting settings.

## `ChatHistory`

```json
"ChatHistory": {
  "SessionsFilePath": "./.looma/chat-sessions.json",
  "SavedAnswersFilePath": "./.looma/saved-answers.json"
}
```

Backs multi-turn chat sessions (`IChatUseCase`) and saved-answer artefacts
(`ISavedAnswerUseCase`) — both local JSON files, same convention as
`AnswerCache.FilePath`. Whole-file read/rewrite on every write, not built
for a large number of sessions; fine for one local user's history.

**Both modes.** In Standalone mode these files back `IChatUseCase`/
`ISavedAnswerUseCase` directly. In McpClient mode they're read/written by
the same local files via `RemoteChatUseCase` — only generation
(retrieval + the LLM call) actually goes to the remote server, via the
`looma_chat` MCP tool; the session/history files themselves are never
sent anywhere (see `docs/mcp-server.md`'s "Chat session storage"
section). Voice input and image-attach captioning also work in both
modes now, via the `looma_transcribe`/`looma_caption_image` MCP tools in
McpClient mode.

## `Security`

```json
"Security": {
  "AllowedInferenceHosts": ["localhost", "127.0.0.1", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"],
  "BlockNonLocalEndpoints": true
}
```

| Field | Meaning |
| --- | --- |
| `AllowedInferenceHosts` | Hostnames/CIDR ranges every `Models.*.Endpoint` (Ollama) is checked against at DI-registration time. |
| `BlockNonLocalEndpoints` | `true` by default. When true, any configured inference endpoint whose host isn't in `AllowedInferenceHosts` fails startup outright (`InferenceEndpointNotAllowedException`) rather than silently connecting to it — this is the "no information leaves the system" guarantee enforced in code, not just documented. Setting this `false` is an explicit, auditable opt-out. |

This section only governs outbound calls to inference endpoints
(`Models.*`). It's unrelated to `Mcp.AllowedHosts`, which governs *inbound*
requests to `Looma.MCP.Server` — don't confuse the two.

## `Agents`

```json
"Agents": { "Enabled": false, "Definitions": [] }
```

**Entirely inert**, same as `Redis`. Reserved for the brief's
`Application.Agents` assembly (optional, depends on `Application`, never
the reverse) — not implemented in any milestone so far.
