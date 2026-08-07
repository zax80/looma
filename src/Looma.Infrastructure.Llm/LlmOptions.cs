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
    /// Only meaningful for <see cref="LlmOptions.BaseModel"/>. Reasoning
    /// ("thinking") models like Qwen3 auto-enable a hidden chain-of-thought
    /// pass by default via Ollama's OpenAI-compatible endpoint — real
    /// generation time spent before the first visible answer token, which
    /// shows up as a long silent pause, not slower streaming. Defaults to
    /// disabling it, since Looma's `answer` is grounded RAG Q&A ("answer
    /// using only the provided context"), not open-ended reasoning — set
    /// this false if a harder synthesis question needs the model to reason.
    /// </summary>
    public bool DisableThinking { get; set; } = true;

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
}
