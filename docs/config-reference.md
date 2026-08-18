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
| `ContextSize` | `BaseModel` only, optional — the model's real context window in tokens. Doesn't configure the model itself (Looma can't reliably set that per request); instead it bounds how much prior conversation `looma chat` sends on each turn, dropping the oldest turns first once the total would exceed it. Left unset, no trimming happens. |
| `Dimensions` | Embedding vector size — `768` for `EmbeddingModel` (nomic-embed-text), `512` for `ImageEmbeddingModel` (CLIP ViT-B/32). Used to size the Qdrant collection. |
| `DisableThinking` | `BaseModel` only. `true` by default — suppresses Qwen3's hidden chain-of-thought pass, since `answer` is grounded Q&A, not open-ended reasoning. |
| `ModelPath` | `Local.*` providers only — where the model file lives on disk (relative paths resolve against the working directory). |
| `DownloadUrl` | `Local.*` providers only — direct HTTP source `LocalModelFileProvisioner` fetches `ModelPath` from on first run if it's missing. Best-effort, not fatal. |
| `TimeoutSeconds` | HTTP timeout for calls to this model. Defaults to `600` — the OpenAI SDK's own 100s default is sized for a cloud API, not a local model that might still be loading or running on CPU; a real captioning call hit exactly this. |
| `TextTower` | `ImageEmbeddingModel` only, and optional — enables `looma search --collection images "<text>"` (text→image search) via CLIP's paired text encoder. See `docs/model-setup.md`'s "Text→image search" section for the full shape (`ModelPath`/`DownloadUrl`/`VocabPath`/`VocabDownloadUrl`/`MergesPath`/`MergesDownloadUrl`) — verified against a real model and image; note its cross-modal scoring caveat (scores run lower than `MinRelevanceScore`'s text-vs-text calibration) before relying on the default threshold. |

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

## `WebSearch`

```json
"WebSearch": {
  "Endpoint": "https://html.duckduckgo.com",
  "TimeoutSeconds": 10
}
```

| Field | Meaning |
| --- | --- |
| `Endpoint` | DuckDuckGo's own HTML results page. No account, no API key, nothing to run separately. Overridable only if DuckDuckGo ever blocks requests from your network and you want to point this at a mirror/proxy. |
| `TimeoutSeconds` | HTTP timeout for the search call. |

**Connection details only** — whether this is ever actually used is
`RAG.EnableWebSearch` below, a separate toggle. This section is always
bound and `IWebSearchProvider` is always registered regardless of that
flag: `DuckDuckGoWebSearchProvider` fails closed to an empty result list
on any connectivity or parsing failure rather than throwing (unlike
`VectorStore`/Qdrant, which fails loudly — see `docs/mcp-server.md`'s
"Qdrant unreachable" section). That asymmetry is deliberate: Qdrant is
the primary retrieval path, web search is an opt-in fallback for when it
already came up empty, so a broken or blocked search backend should
degrade to "no web results either," not break the turn.

**How this actually works, and its real limits.** DuckDuckGo has no
official public web-search API — `api.duckduckgo.com`'s "Instant Answer"
API only returns definition/disambiguation-style boxes for a narrow set
of queries, not ranked results for an arbitrary question. Getting real
web results without an API key means fetching and parsing the same HTML
page a browser gets, which is what `DuckDuckGoWebSearchProvider` does
(plain `HttpClient`, no HTML-parsing library — see the project's own
`.csproj` comment for why: this was built specifically to avoid adding
*any* new dependency, library or hosted API, for web search). That
carries two real, inherent caveats, independent of how carefully the
parsing code is written:

- **Unofficial and unversioned.** DuckDuckGo can change this page's
  markup at any time with no notice, which would break the regex-based
  parser in `Internal/DuckDuckGoResultParser.cs` until it's updated
  against the new markup.
- **Rate-limited/throttled for automated-looking traffic.** Frequent
  requests may get throttled or blocked outright — there's no quota to
  buy your way past this, unlike a paid search API.

Because of the first point, this was implemented against DuckDuckGo's
long-documented (years-stable) result markup but could not be verified
against a live response — the sandbox this was built in has no outbound
access to duckduckgo.com. If `RAG.EnableWebSearch` is on and web results
never show up despite the local index genuinely having nothing relevant,
check whether `DuckDuckGoWebSearchProvider.SearchAsync` is getting a
non-200 response (blocked) or a 200 with zero results parsed out of it
(markup drift) — the fix differs.

## `Mcp`

```json
"Mcp": {
  "Enabled": false,
  "Auth": { "Mode": "ApiKey", "ApiKeyEnvVar": "LOOMA_MCP_API_KEY" },
  "AllowedHosts": ["localhost", "127.0.0.1", "::1"],
  "Tls": {
    "Enabled": false,
    "CertificatePath": null,
    "CertificatePasswordEnvVar": null
  }
}
```

Full detail in `docs/mcp-server.md`. Summary:

| Field | Meaning |
| --- | --- |
| `Enabled` | **Inert** — not read by `Looma.MCP.Server` (which starts whenever you run it, regardless of this flag) or `Looma.CLI` (mode selection is entirely `Deployment.Mode`, not this flag). Kept for shape-compatibility with the brief; may get wired to something later. |
| `Auth.Mode` | Must be `"ApiKey"` — the only mode `Looma.MCP.Server` and `Looma.MCP.Client` support so far. Anything else fails at startup on both sides. |
| `Auth.ApiKeyEnvVar` | Name of the environment variable holding the shared API key (not the key itself — never put a real key in config.json). Both server and client read the *same* env var name from their own process's environment; the actual key value must match between the two processes. |
| `AllowedHosts` | Host-header allow-list the server checks on every request (DNS-rebinding defense). Defaults to loopback-only if omitted. |
| `Tls.Enabled` | Opt-in, `false` by default — plain HTTP on localhost needs no cert. When `true`, the server listens on `https://` instead of `http://` (same default port). |
| `Tls.CertificatePath` | Path to a PFX/P12 certificate file. Left `null`, Kestrel falls back to the ASP.NET Core HTTPS developer certificate (`dotnet dev-certs https`) — real TLS, just self-signed; any client machine needs `dotnet dev-certs https --trust` run once, or the handshake fails. Set this to a real certificate once this crosses a network boundary that matters. |
| `Tls.CertificatePasswordEnvVar` | Name of an environment variable holding the PFX's password, if it has one. Same never-put-the-secret-in-config.json convention as `Auth.ApiKeyEnvVar`. Ignored when `CertificatePath` is `null`. |

## `RAG`

```json
"RAG": {
  "Sources": [{ "Type": "FileSystem", "Path": "./data", "FileTypes": [...], "Recursive": true }],
  "ChunkSize": 800,
  "ChunkOverlap": 100,
  "TopK": 5,
  "MinRelevanceScore": 0.55,
  "MaxAnswerTokens": null,
  "AnswerTemperature": 0.1,
  "EnableQueryReformulation": true,
  "EnableAdaptiveThreshold": true,
  "AdaptiveFloorScore": 0.40,
  "AdaptiveThresholdMargin": 0.15,
  "EnableWebSearch": false,
  "WebSearchMaxResults": 3,
  "WebSearchGenerationTimeoutSeconds": 60,
  "EnableHybridSearch": false,
  "HybridRelevanceRatio": 0.5
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
| `EnableQueryReformulation` | Chat only (not `answer`/`search`, which have no history to reformulate against). On by default: rewrites a follow-up like "who's the author?" into a standalone search query using recent conversation history, before embedding it for retrieval — see `ChatCompletionUseCase`'s doc comment. Costs one extra non-streaming LLM call before the visible answer starts streaming, on every turn after the first. Set `false` if that added latency matters more than occasionally poor multi-turn retrieval on your hardware. |
| `EnableAdaptiveThreshold` | Chat only, same scope as `EnableQueryReformulation`. On by default: instead of a flat `MinRelevanceScore` cutoff applied identically to every query, keeps whichever of the TopK candidates score within `AdaptiveThresholdMargin` of THAT query's own best match — a hard/terse query's best hit at 0.58 can still keep a genuinely close second at 0.56, while an easy query's runner-up at 0.60 gets cut if its best hit was 0.85. No extra retrieval or LLM call. Can only prune what a flat floor would've kept, never add what it would've excluded, so there's little reason to turn this off. |
| `AdaptiveFloorScore` | The floor `EnableAdaptiveThreshold` searches against instead of `MinRelevanceScore` — deliberately lower (but still above the observed 0.44-0.50 false-positive band, see `MinRelevanceScore` above) so a hard query's true positive is still fetched as a candidate; `AdaptiveThresholdMargin` then decides whether it's actually kept. Ignored when `EnableAdaptiveThreshold` is `false`. |
| `AdaptiveThresholdMargin` | How far below the best-scoring candidate a runner-up can fall and still be kept, when `EnableAdaptiveThreshold` is on. Reasoned starting point (0.15), not measured-optimal — same "validate with `looma search` before tuning further" caveat as `MinRelevanceScore`. |
| `AnswerTemperature` | Chat sampling temperature for `answer`. Low (`0.1`) by default, deliberately — grounded Q&A should stick to the provided context, not free-associate. |
| `EnableWebSearch` | Both `answer` and chat. `false` by default (opt-in — see the `WebSearch` section above for what this actually does under the hood and its real caveats). When `true`, a LOCAL-ONLY answer that comes back as the refusal sentence triggers a web search and a second generation pass using the web results instead — decided by watching what the model actually generates (`RefusalPrefixWatcher`), not by citation count (see `WebSearchFallback`'s doc comment for why). Still streams live: only the refusal sentence itself (short, fixed-length) is ever briefly held back, not the whole answer. Web results are cited the same way as document chunks, just with a URL instead of a line range. |
| `WebSearchMaxResults` | How many web results to fold into the context when the fallback above triggers. Deliberately smaller than `TopK` — this fills a gap in the local index, not competing with it on breadth. |
| `WebSearchGenerationTimeoutSeconds` | IDLE timeout, in seconds — resets on every streamed chunk, not a total-time budget — applied to generation calls made when `EnableWebSearch` is on. See `IdleTimeoutStreaming`'s doc comment. Turns a stalled/looping local model into a visible failure instead of an indefinite silent hang, without killing a slow-but-still-producing answer. `60` by default. |
| `EnableHybridSearch` | Both `answer` and chat. `false` by default — combines vector similarity with keyword (BM25) matching via Qdrant's Query API (Reciprocal Rank Fusion) instead of vector similarity alone, catching exact-term matches (product codes, names, acronyms) pure vector search can miss. Turning this on for the first time is a real, breaking Qdrant schema change: it recreates the `documents` collection (emptying it — re-run `looma index` afterward) and needs Qdrant 1.15.2+ for server-side BM25, checked at startup with a clear error if too old. Also disables `MinRelevanceScore`/`EnableAdaptiveThreshold` filtering for retrieval, since a fused (RRF) score isn't a cosine similarity those are calibrated against — `HybridRelevanceRatio` below replaces them. |
| `HybridRelevanceRatio` | Only used when `EnableHybridSearch` is on. Keeps only candidates whose fused (RRF) score is at least this fraction of THAT query's own best-scoring candidate — the hybrid-search equivalent of `AdaptiveThresholdMargin`, rescaled as a ratio since RRF scores are small, k-constant-dependent fractions an absolute margin doesn't transfer to. `0.5` is a reasoned starting point, not measured-optimal. Without this, `TopK` alone bounded a hybrid result, which let unrelated low-ranked candidates pad out the result set — a real issue, not theoretical. |

## `AnswerCache`

```json
"AnswerCache": {
  "Enabled": true,
  "FilePath": "./.looma/answer-cache.json",
  "CollectionName": "answer_cache",
  "SemanticSimilarityThreshold": 0.97,
  "WebCitationMaxAgeHours": 24,
  "MaxExactEntries": 500
}
```

| Field | Meaning |
| --- | --- |
| `Enabled` | Toggles caching. Reuses `VectorStore.Endpoint`/`ApiKey` (same Qdrant instance) — this section only adds cache-specific settings. |
| `FilePath` | Exact-match layer: a local JSON file keyed by normalized question text. Resolved relative to the working directory. Kept in memory after first load for the rest of the process's lifetime (this process is the file's sole writer), so cache hits after the first never touch disk again. |
| `CollectionName` | Semantic-fallback layer: a dedicated Qdrant collection for cached question embeddings — deliberately separate from `documents`/`images` (never mix embedding purposes in one collection). |
| `SemanticSimilarityThreshold` | Cosine similarity a candidate question must clear to count as "the same question." Deliberately strict (`0.97`) — a false positive here means confidently serving a wrong answer, worse than the latency the cache exists to avoid. |
| `WebCitationMaxAgeHours` | Hours after which a cached answer citing at least one web search result is treated as stale and no longer served, on top of the existing re-index invalidation (which a web citation's real-world content can outlive without ever changing the local chunk count). Answers sourced entirely from local documents are unaffected. `null` disables this check entirely. Default `24`, a reasoned starting point rather than a measured-optimal value. |
| `MaxExactEntries` | Soft cap on the exact-match layer's entry count. Once exceeded, the oldest entries (by creation time, not last-accessed — true LRU would add a write-back cost to every lookup) are evicted on the next write until back under the cap. Zero or less disables eviction (unbounded growth). The semantic (Qdrant) layer is not capped by this — it scales with point count far better than repeatedly rewriting a flat JSON file. Default `500`. |

Run `looma clear-cache` after a prompt/model/config change that a re-index
wouldn't otherwise invalidate — the cache's staleness check only tracks
re-indexing and (for web-cited answers) `WebCitationMaxAgeHours`, not other
generation-affecting settings.

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
