# Looma.MCP.Server and Looma.MCP.Client

`Looma.MCP.Server` exposes Looma's indexing/search/answer/count/cache
operations as MCP tools over HTTP. `Looma.MCP.Client` is a real MCP client —
Looma.CLI can run in either "Standalone" mode (talks to Qdrant/Ollama
directly, in-process) or "McpClient" mode (talks to a remote
Looma.MCP.Server instead, via `Looma.MCP.Client`), selected by
`Deployment:Mode` in `config.json`. Command code under `Looma.CLI/Commands`
is identical either way — it only ever sees `Looma.Application`'s use-case
interfaces, never which mode is active.

## Scope of this milestone

- **Transport: HTTP by default, HTTPS opt-in via `Mcp.Tls.Enabled`.**
  Plain HTTP remains the default — the common case is one local user on
  `localhost`, nothing to encrypt against itself. Enabling TLS is a config
  change, not a code change; see "Enabling TLS" below. Still don't expose
  this server beyond `localhost` on plain HTTP without either enabling
  `Mcp.Tls` or putting a TLS-terminating reverse proxy in front of it.
- **Auth: API key only**, enforced on *every* request — including tool
  listing, not just tool calls. There's no anonymous path.
- **Host-header validation** against a configurable allow-list, as a
  DNS-rebinding defense (Kestrel doesn't validate `Host` headers on its
  own).

## Running it

1. Set the API key the server will require. `config.json`'s
   `Mcp.Auth.ApiKeyEnvVar` names the environment variable (default
   `LOOMA_MCP_API_KEY`) — the server refuses to start if it's unset:

   ```powershell
   # PowerShell
   $env:LOOMA_MCP_API_KEY = "<a long random string>"
   ```

   ```bash
   # bash
   export LOOMA_MCP_API_KEY="<a long random string>"
   ```

2. **Don't use `dotnet run --project` for this one — launch the built DLL
   directly instead.** `Looma.MCP.Server` uses `Microsoft.NET.Sdk.Web` (the
   ASP.NET Core SDK), and — confirmed via a real run, not a theoretical
   concern — `dotnet run --project` for a Web SDK project sets the
   process's actual working directory to the *project's own folder*,
   regardless of the invoking shell's directory and with no
   `launchSettings.json` involved at all. That breaks more than just
   finding `config.json`: `RAG.Sources[0].Path` (`./data`) is also resolved
   against that same (wrong) working directory, so indexing silently finds
   zero files — passing `--config` alone isn't enough to fix this.
   `Looma.CLI` (plain console SDK) doesn't have this quirk, so the
   identical-looking `dotnet run --project src/Looma.CLI` from the repo
   root works fine; the server's won't. `dotnet <dll>` has no such
   SDK-specific behavior — it just inherits the shell's real CWD, like any
   normal process — so build once, then run the DLL from the repo root:

   ```powershell
   # PowerShell, from the repo root
   dotnet build
   dotnet src\Looma.MCP.Server\bin\Debug\net10.0\looma-mcp-server.dll
   ```

   ```bash
   # bash, from the repo root
   dotnet build
   dotnet src/Looma.MCP.Server/bin/Debug/net10.0/looma-mcp-server.dll
   ```

   No `--config` needed this way — both `config.json` and `RAG.Sources[0].Path`
   resolve correctly against the repo root. `--config <path>` still works
   if you need to point at a config file somewhere else.

   First run does the same model auto-provisioning the CLI does (Ollama
   pull for Base/Embedding models — fatal if it fails; Vision model, CLIP,
   and Whisper — best-effort, only block the media types that need them).

3. By default it listens on `http://localhost:3001`. Override with
   `ASPNETCORE_URLS` or `--urls` if needed.

## Enabling TLS

Off by default (see "Scope of this milestone" above). Two ways to turn it
on, both via `config.json`'s `Mcp.Tls` section — no code changes:

**Self-signed dev cert (quickest — good for a private LAN, not a public
network):**

```json
"Tls": { "Enabled": true, "CertificatePath": null, "CertificatePasswordEnvVar": null }
```

Leaving `CertificatePath` unset makes Kestrel fall back to the standard
ASP.NET Core HTTPS developer certificate — the same one `dotnet dev-certs
https` manages. It's a real TLS handshake (traffic is genuinely
encrypted), just with a certificate no one outside your own machine trusts
yet. Any machine that will *connect* to the server (including one running
`Looma.MCP.Client`/`Looma.CLI` in McpClient mode) needs to trust it once:

```powershell
dotnet dev-certs https --trust
```

The server then listens on `https://localhost:3001` by default (same port
unless overridden). Set `Deployment:McpServerEndpoint` to
`"https://<host>:3001"` on the client side to match.

**A real certificate (production-appropriate, once this crosses a network
boundary that matters):**

```json
"Tls": {
  "Enabled": true,
  "CertificatePath": "C:\\path\\to\\cert.pfx",
  "CertificatePasswordEnvVar": "LOOMA_TLS_CERT_PASSWORD"
}
```

`CertificatePath` must be a PFX/P12 file. If it's password-protected, set
the env var named in `CertificatePasswordEnvVar` to that password before
starting the server (same never-put-secrets-in-config.json convention as
`Mcp.Auth.ApiKeyEnvVar`). A missing file or a load failure (wrong
password, corrupt file) fails startup with a clear error rather than
silently falling back to the dev cert.

## Connecting a client

Any MCP client that supports streamable HTTP transport works. Point it at
`http://localhost:3001` and set one of these headers on every request:

- `Authorization: Bearer <your API key>` (preferred), or
- `X-Api-Key: <your API key>`

If the client's Host header isn't in `Mcp.AllowedHosts` (defaults to
`localhost`, `127.0.0.1`, `::1`), the request is rejected before it ever
reaches MCP request handling.

Verify against a real client — e.g. the
[MCP Inspector](https://github.com/modelcontextprotocol/inspector), or
Claude Desktop's custom MCP server config — before relying on this.

## Tools exposed

| Tool | Wraps | Notes |
| --- | --- | --- |
| `looma_index` | `IIndexingUseCase` | Streams a progress notification per file; destructive if `clearFirst=true`. |
| `looma_search` | `ISearchUseCase` | `collection="images"` currently only supports image-to-image queries — no CLIP text encoder is wired up yet for text→image search. |
| `looma_answer` | `IAnswerUseCase` | Streams generated tokens as progress notifications; final result includes citations. May include web-search citations (`MediaType.Web`, a URL instead of a line range) if `RAG.EnableWebSearch` is on and local retrieval found nothing — see `docs/config-reference.md`'s `WebSearch` section. |
| `looma_count` | `ICountUseCase` | Single call, no streaming; returns the raw count as plain text. |
| `looma_clear_cache` | `IAnswerCache.ClearAsync` | Same operation as the CLI's `clear-cache` command. |
| `looma_chat` | `IChatCompletionUseCase` | Stateless: caller supplies the full prior-turn history (`historyJson`) on every call. Chat sessions themselves live client-side only — see below. Same web-search fallback as `looma_answer` above. |
| `looma_transcribe` | `ITranscriptionUseCase` | Single call, no streaming. Audio travels as base64 (`audioBase64`); returns the transcript as plain text. |
| `looma_caption_image` | `IImageCaptionUseCase` | Single call, no streaming. Image travels as base64 (`imageBase64`); returns `ImageCaptionResult` as JSON. |
| `looma_extract_document` | `IDocumentExtractionUseCase` | Single call, no streaming. Document travels as base64 (`documentBase64`) plus `fileName` (for its extension); returns extracted plain text. |

Document *export* (turning a chat answer into a real .docx/.md/.txt/.pdf
file, `IDocumentExportUseCase`) has no MCP tool at all, deliberately — it's pure
local text formatting of an answer the client already has in hand, no
Qdrant/Ollama involved, so `Looma.MCP.Client` registers the exact same
`DocumentExportUseCase` Standalone mode uses rather than calling out to
the server for it.

All of these forward real-time progress via MCP's `notifications/progress`
mechanism (tied to the caller's `progressToken`, works in stateless HTTP
mode per the SDK's own docs) rather than buffering a whole run before
returning — the same "no buffer-and-return shortcut" rule the underlying
`Looma.Application` use-case interfaces already document for local (CLI)
consumers.

Each progress notification's `Message` field is JSON — specifically, the
actual `Looma.Core.Entities` record being streamed (`IndexingProgress`,
`VectorSearchResult`, or `AnswerToken`, citations' embedding vectors
stripped), not prose. `Looma.MCP.Client` deserializes straight back into
that same type. A generic MCP client (Inspector, Claude Desktop) still
shows it fine — it's just JSON instead of a formatted sentence — and the
final `CallToolResult` text is a human-readable summary either way.

## Connecting via Looma.CLI (McpClient mode)

Instead of a generic MCP client, `Looma.CLI` can run against a remote
Looma.MCP.Server directly, using the exact same `index`/`answer`/`count`/
`search`/`clear-cache` commands as standalone mode. To switch a `config.json`
into this mode:

```json
"Deployment": {
  "Mode": "McpClient",
  "McpServerEndpoint": "http://localhost:3001"
}
```

Then run the CLI with the same API key env var set as the server:

```powershell
$env:LOOMA_MCP_API_KEY = "<the server's key>"
dotnet run --project src/Looma.CLI -- answer "what's in the image?"
```

In this mode the CLI does none of its own Ollama/model-provisioning work —
the remote server owns that entirely — it just connects and calls tools.
`Mcp:Auth:Mode` must be `"ApiKey"` (the only mode `Looma.MCP.Client`
supports so far); anything else fails at startup rather than silently
connecting unauthenticated.

## Qdrant unreachable

If Qdrant itself is down or unreachable when a tool that needs it runs
(`looma_chat`, `looma_answer`, `looma_search`, `looma_index`, `looma_count`,
`looma_clear_cache`), the client gets a clear
`"Can't reach Qdrant to <action> — make sure Qdrant is running..."` message
instead of a generic, unhelpful `"An error occurred invoking 'x'"`. Nothing
touches Qdrant at server startup — the server and any connected client
start cleanly regardless; the error only surfaces the moment an actual
request needs retrieval. See `VectorStoreUnavailableException`'s doc
comment for the full mechanism (a real, reproduced case: stopping Qdrant
mid-session and asking a chat question).

## Known gaps (not fixed here)

- TLS is opt-in, not enforced — a server started with `Mcp.Tls.Enabled`
  left `false` (the default) is still plain HTTP, and nothing warns beyond
  the startup banner. No automatic HTTP→HTTPS redirect either.
- `looma_search --collection images` with a natural-language query now
  works if `Models.ImageEmbeddingModel.TextTower` is configured (see
  `docs/model-setup.md`'s "Text→image search" section) — otherwise it
  fails with a clear "not configured" error rather than Qdrant's confusing
  dimension-mismatch message. Verified against a real model and image;
  note the cross-modal scoring caveat in the same doc section before
  relying on the default `MinRelevanceScore`.
- No rate limiting or connection-count limits.
- `Looma.MCP.Client` doesn't reconnect or retry on a dropped connection —
  if the server restarts mid-session, the CLI process needs to be re-run.
- `Looma.MCP.Client`'s `IAnswerCache` adapter only implements `ClearAsync`
  (the only method the CLI ever calls directly); the other three methods
  throw, since real caching happens entirely server-side inside the remote
  `looma_answer` tool in this mode. Documented on the adapter itself.
- Voice input, image-attach captioning, and document-attach extraction all
  use base64-encoded whole-file tool parameters (`looma_transcribe`/
  `looma_caption_image`/`looma_extract_document` above) rather than a
  proper binary/streaming transport — fine for a single short voice clip,
  image, or document, but not something to build a bulk upload path on.
- The "offer a document export" trigger (`DocumentGenerationIntentDetector`)
  is plain keyword matching on the user's message, not real intent
  understanding — see its own doc comment for the specific heuristic and
  its known false-negative bias.

## Chat session storage (client-side only)

`looma_chat` is deliberately stateless — it takes a full history and
returns one reply, nothing more. Session persistence (`IChatUseCase`'s
`StartSessionAsync`/`ListSessionsAsync`/etc.) is handled entirely by
`Looma.MCP.Client.RemoteChatUseCase` using the same local
`IChatSessionStore` Standalone mode uses (see `config-reference.md`'s
`ChatHistory` section) — nothing about a conversation's text needs
Qdrant or Ollama, so there's no reason to make the server stateful for
it. This also means chat sessions created in McpClient mode live only on
the machine that created them, same as Standalone mode.
