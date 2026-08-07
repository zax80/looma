# Looma.MCP.Server

Standalone MCP server exposing Looma's indexing/search/answer/count/cache
operations as MCP tools over HTTP. This is the "server first" half of
milestone 6 — `Looma.MCP.Client` and CLI MCP-client mode (switching
`Deployment.Mode` to talk to a remote server instead of running everything
in-process) are a follow-up, not covered here.

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
| `looma_count` | `ICountUseCase` | Single call, no streaming. |
| `looma_clear_cache` | `IAnswerCache.ClearAsync` | Same operation as the CLI's `clear-cache` command. |

All of these forward real-time progress via MCP's `notifications/progress`
mechanism (tied to the caller's `progressToken`, works in stateless HTTP
mode per the SDK's own docs) rather than buffering a whole run before
returning — the same "no buffer-and-return shortcut" rule the underlying
`Looma.Application` use-case interfaces already document for local (CLI)
consumers.

## Known gaps (not fixed here)

- No TLS (see above).
- `looma_search --collection images` with a natural-language query fails
  with a dimension-mismatch error rather than a helpful message — same
  underlying gap `ISearchUseCase`'s doc comment already flags for the CLI's
  `search` command.
- No rate limiting or connection-count limits.
