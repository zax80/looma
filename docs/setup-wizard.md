# Setup wizard (`looma setup`)

Creates or updates `config.json` from a handful of high-level answers,
instead of hand-writing the full file from `docs/config-reference.md`.
CLI-first — a MAUI Desktop equivalent may follow later, same scoping as the
rest of this feature.

## Running it

```powershell
looma setup
```

Interactively prompts for each answer, showing the current value (from an
existing `config.json`) or a sensible default in brackets — press Enter to
accept it. Any answer can also be passed as a flag to skip that prompt:

```powershell
looma setup --domain "Legal review" --location Bulgaria --mode Standalone --scale false
```

| Flag | Meaning |
| --- | --- |
| `--domain <name>` | Free-text label, saved as `DomainName`. Also picks a quality/speed tier for `Models.BaseModel` — see "Model recommendations" below. |
| `--location <place>` | Free-text, not saved directly — picks `Models.SpeechToTextModel.Language` and, for a handful of known locations, a region-tuned base model. |
| `--mode Standalone\|McpClient` | Sets `Deployment.Mode`. `Standalone` talks to Qdrant/Ollama directly; `McpClient` talks to a remote `Looma.MCP.Server` (see `--mcp-endpoint` and `docs/mcp-server.md`). If you're setting up the server machine itself, pick `Standalone` here too — `Looma.MCP.Server` doesn't read `Deployment.Mode` at all, it always serves; every *other* machine's config should be `McpClient` pointing at it. |
| `--mcp-endpoint <url>` | Only asked/used when `--mode McpClient`. Sets `Deployment.McpServerEndpoint`. |
| `--scale true\|false` | Sets `Redis.Enabled` — see "Horizontal scale" below. |

Running with no input available at all (piped/redirected stdin, e.g. in a
script or CI) never hangs — every prompt falls back to its default instead
of blocking on a read that will never come.

## What it does — and doesn't — touch

The wizard only ever writes: `DomainName`, `Deployment.Mode` (+
`McpServerEndpoint`), `Models.BaseModel.Model`/`SupportsThinking`,
`Models.VisionModel.Model`, `Models.EmbeddingModel.Model`,
`Models.SpeechToTextModel.Language`, and `Redis.Enabled`. Re-running it
against a config.json you've already hand-tuned (RAG thresholds, chunk
size, TLS certificate settings, cache sizes, etc.) leaves everything else
exactly as it was — it's a targeted merge, not an overwrite.

**Everything the wizard doesn't ask about is "advanced settings": edit
config.json directly.** See `docs/config-reference.md` for every field. The
wizard is meant to get a working config fast, not to be the only way to
configure Looma.

## Model recommendations

Two independent axes:

- **Location** picks the spoken language and, for a short list of known
  locations, a region-tuned chat model that's actually been exercised
  end-to-end in this codebase — currently just Bulgaria → the BgGPT model
  already used as this project's own default. An unrecognized location
  falls back to `auto` language detection and the generic model below.
- **Domain** picks a quality/speed tier — a generic heuristic (the same one
  the Ask/Think toggle is built on), not a claim of domain-specific model
  training:
  - Legal, medical, compliance, research, finance, audit → larger model,
    reasoning ("Think") enabled by default.
  - Support, helpdesk, FAQ, customer service → smaller, faster model.
  - Anything else → the balanced default (`qwen3:8b`), same as
    `docs/looma-project-brief.md` section 8's original default.

A location's regional override always wins over a domain's generic tier
model — a verified region-tuned model beats a generic size guess. Both
axes are free-text, keyword-matched case-insensitively; there's no fixed
dropdown to work around.

These are starting points, not guarantees. An unavailable or mistyped
Ollama tag fails loudly and specifically the first time Looma actually
tries to pull it (same as any other model-name typo in config.json) — it's
not a special failure mode of the wizard, just a wrong config value like
any other, fixable in config.json afterward.

## Horizontal scale

`--scale true` sets `Redis.Enabled: true` in config.json. As of this
milestone, that's **only a recorded intent** — the actual horizontal-scaling
backend (indexing job queue, pub/sub progress fan-out, shared embedding
cache) described in `docs/looma-project-brief.md` section 3 isn't
implemented yet. The wizard's job here is to configure, not provision —
flipping this on doesn't start or require a Redis instance.
