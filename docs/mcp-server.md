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

- **Transport: plain HTTP, not TLS.** This is a deliberate, flagged
  deferral, not an oversight — the brief's security posture calls for TLS
  always, even on private networks. Don't expose this server beyond
  `localhost` without putting a TLS-terminating reverse proxy in front of
  it. Self-signed dev cert setup is a separate, well-scoped follow-up task.
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

2. Run from the directory containing `config.json` (same convention as the
   CLI — `--config <path>` also works):

   ```
   dotnet run --project src/Looma.MCP.Server
   ```

   First run does the same model auto-provisioning the CLI does (Ollama
   pull for Base/Embedding models — fatal if it fails; Vision model, CLIP,
   and Whisper — best-effort, only block the media types that need them).

3. By default it listens on `http://localhost:3001`. Override with
   `ASPNETCORE_URLS` or `--urls` if needed.

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
| `looma_answer` | `IAnswerUseCase` | Streams generated tokens as progress notifications; final result includes citations. |
| `looma_count` | `ICountUseCase` | Single call, no streaming; returns the raw count as plain text. |
| `looma_clear_cache` | `IAnswerCache.ClearAsync` | Same operation as the CLI's `clear-cache` command. |

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

## Known gaps (not fixed here)

- No TLS (see above).
- `looma_search --collection images` with a natural-language query fails
  with a dimension-mismatch error rather than a helpful message — same
  underlying gap `ISearchUseCase`'s doc comment already flags for the CLI's
  `search` command.
- No rate limiting or connection-count limits.
- `Looma.MCP.Client` doesn't reconnect or retry on a dropped connection —
  if the server restarts mid-session, the CLI process needs to be re-run.
- `Looma.MCP.Client`'s `IAnswerCache` adapter only implements `ClearAsync`
  (the only method the CLI ever calls directly); the other three methods
  throw, since real caching happens entirely server-side inside the remote
  `looma_answer` tool in this mode. Documented on the adapter itself.
