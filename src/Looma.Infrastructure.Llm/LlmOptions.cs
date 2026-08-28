namespace Looma.Infrastructure.Llm;

/// <summary>
/// Binds to the <c>Models</c> section of config.json (see
/// <c>docs/looma-project-brief.md</c> section 8). <see cref="BaseModel"/>,
/// <see cref="EmbeddingModel"/>, <see cref="VisionModel"/> (captioning/OCR),
/// and <see cref="ImageEmbeddingModel"/> (CLIP) are all wired up now.
/// <see cref="SpeechToTextModel"/> is still bound for shape-completeness
/// only — audio/Whisper ingestion is a later sub-milestone.
/// </summary>
public sealed class LlmOptions
{
    public const string SectionName = "Models";

    public ModelEndpointOptions BaseModel { get; set; } = new();
    public ModelEndpointOptions EmbeddingModel { get; set; } = new();
    public ModelEndpointOptions VisionModel { get; set; } = new();
    public ModelEndpointOptions ImageEmbeddingModel { get; set; } = new();
    public ModelEndpointOptions SpeechToTextModel { get; set; } = new();
}

public sealed class ModelEndpointOptions
{
    public string Provider { get; set; } = "Ollama";
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// The context window (in tokens) <see cref="Model"/> actually has.
    /// Looma doesn't — and, per a real provider-endpoint limitation hit
    /// with Ollama's OpenAI-compatible surface, in general can't reliably —
    /// tell the serving model to USE a given window per request; what this
    /// value is actually for is letting Looma bound how much conversation
    /// history it sends in the first place, so a long session can never
    /// silently exceed whatever window the model really has. See
    /// <c>Looma.Application.Internal.ConversationHistoryBudget</c> — the
    /// generic, provider-agnostic fix for a real, reproduced bug (a long
    /// chat session's accumulated history exceeded the model's real
    /// window, and every subsequent turn broke, including ones on a
    /// totally unrelated topic, because nothing was trimming what got
    /// sent). Left null, no trimming happens — fine for a model whose
    /// context comfortably exceeds any conversation you'll realistically
    /// have, risky otherwise.
    /// </summary>
    public int? ContextSize { get; set; }

    public int? Dimensions { get; set; }

    /// <summary>Only meaningful for <c>Local.*</c> providers (Whisper, CLIP) — not used by the chat/embedding path.</summary>
    public string? ModelPath { get; set; }

    /// <summary>
    /// Only meaningful for <c>Local.*</c> providers. Direct HTTP source
    /// <see cref="LocalModelFileProvisioner"/> fetches <see cref="ModelPath"/>
    /// from if the file doesn't already exist locally — the brief's "direct
    /// ONNX fetch for Whisper/CLIP" half of first-run auto-provisioning
    /// (docs/looma-project-brief.md section 6), as opposed to the
    /// Ollama-pull half that <see cref="OllamaStartup"/> handles for
    /// Ollama-served models.
    /// </summary>
    public string? DownloadUrl { get; set; }

    /// <summary>
    /// Only meaningful for <see cref="LlmOptions.BaseModel"/>. Whether this
    /// model can be asked to reason ("think") before answering at all —
    /// reasoning models like Qwen3 auto-enable a hidden chain-of-thought
    /// pass by default via Ollama's OpenAI-compatible endpoint (real
    /// generation time spent before the first visible answer token, which
    /// shows up as a long silent pause, not slower streaming), while a
    /// model like Gemma3/BgGPT has no such mode at all. Explicit config,
    /// not auto-detected from the model name — same "don't guess, be told"
    /// stance as the rest of this options class.
    ///
    /// Defaults to <c>false</c> (safe: never sends a
    /// <c>reasoning_effort</c> field the model wasn't asked about). When
    /// <c>true</c>, <see cref="ReasoningEffortChatClient"/> is wired in and
    /// honors a per-request <c>Looma.Core.Entities.ThinkingMode</c> — see
    /// its own doc comment — defaulting to <c>Ask</c> (no reasoning) unless
    /// a caller explicitly opts into <c>Think</c> for that one request
    /// (CLI <c>--think</c>, an MCP tool's <c>think</c> parameter, or the
    /// MAUI Desktop chat UI's toggle). Set this <c>true</c> for a
    /// reasoning-capable model like Qwen3 to make that toggle available;
    /// leave it <c>false</c> for a model with no reasoning mode (e.g.
    /// Gemma3/BgGPT) — the toggle is then hidden/disabled client-side and
    /// a stray request for <c>Think</c> is silently treated as <c>Ask</c>.
    /// </summary>
    public bool SupportsThinking { get; set; }

    /// <summary>
    /// HTTP network timeout for calls to this model, in seconds. The OpenAI
    /// SDK's own default is 100 seconds ("configured timeout of 0:01:40" is
    /// literally that default surfacing in the exception message) — sized
    /// for a cloud API, not a local model that might still be loading into
    /// memory or running on CPU. A real run hit exactly this captioning a
    /// single image with the vision model: it retried 4 times and still
    /// failed, entirely because of the timeout, not a real error. Defaults
    /// to something far more generous for local inference; lower it if your
    /// hardware is fast and you'd rather a hang fail quickly than wait.
    /// </summary>
    public int TimeoutSeconds { get; set; } = 600;

    /// <summary>
    /// Only meaningful for <see cref="LlmOptions.SpeechToTextModel"/> — a
    /// Whisper language code (e.g. <c>"bg"</c>, <c>"en"</c>) to force
    /// transcription into. Defaults to <c>"auto"</c> (Whisper detects the
    /// spoken language itself from the audio). Auto-detect is a real weak
    /// point for a lower-resource language on a short clip — a quick voice
    /// question often doesn't give the model enough audio to detect the
    /// language reliably, on top of (not instead of) whatever accuracy gap
    /// the model size itself has for that language — see
    /// docs/model-setup.md's Whisper section. A bigger model helps the
    /// accuracy side, this setting helps the detection side; they're
    /// independent, both worth doing for a consistently non-English
    /// speaker. Set this to your own language's
    /// code if you always (or almost always) speak the same language to
    /// Looma; leave it <c>"auto"</c> if you switch between languages.
    /// </summary>
    public string Language { get; set; } = "auto";

    /// <summary>
    /// Only meaningful on <see cref="LlmOptions.ImageEmbeddingModel"/>, and
    /// only if text→image search is wanted — everything about it is
    /// optional and best-effort, same as CLIP/Whisper provisioning
    /// generally (see <c>LocalModelFileProvisioner</c>'s doc comment): a
    /// missing/unset <see cref="ClipTextTowerOptions"/> just means
    /// <c>looma search --collection images "&lt;text&gt;"</c> isn't
    /// available, nothing else is affected.
    /// </summary>
    public ClipTextTowerOptions? TextTower { get; set; }
}

/// <summary>
/// CLIP's paired text encoder (see
/// <see cref="Looma.Core.Abstractions.ITextToImageEmbeddingGenerator"/>) —
/// a second ONNX graph plus its own byte-pair-encoding tokenizer files,
/// downloaded from the SAME Xenova/clip-vit-base-patch32 repo as the
/// vision tower (<see cref="ModelEndpointOptions.ModelPath"/> /
/// <see cref="ModelEndpointOptions.DownloadUrl"/>) so the two embedding
/// spaces actually line up. All three files are provisioned the same
/// best-effort way as the vision model — see
/// <c>LocalModelFileProvisioner.EnsureTextToImageSearchModelReadyAsync</c>.
/// </summary>
public sealed class ClipTextTowerOptions
{
    public string? ModelPath { get; set; }
    public string? DownloadUrl { get; set; }
    public string? VocabPath { get; set; }
    public string? VocabDownloadUrl { get; set; }
    public string? MergesPath { get; set; }
    public string? MergesDownloadUrl { get; set; }
}
