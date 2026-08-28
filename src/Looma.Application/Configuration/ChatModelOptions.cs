namespace Looma.Application.Configuration;

/// <summary>
/// Binds to the <c>Models:BaseModel</c> section of config.json.
/// Deliberately a separate, minimal type from
/// <c>Looma.Infrastructure.Llm.LlmOptions</c> — same reasoning as
/// <see cref="EmbeddingModelOptions"/> (Application must not reference an
/// Infrastructure.* project) — it declares just the fields Application
/// actually needs from that config section:
/// <see cref="ContextSize"/> (<c>Looma.Application.Internal.ConversationHistoryBudget</c>'s
/// bound on how much conversation history gets sent) and
/// <see cref="SupportsThinking"/> (<c>Looma.Application.UseCases.IChatModelCapabilities</c>'s
/// local implementation — whether the "Ask"/"Think" toggle is available at
/// all for the configured model; see
/// <c>Looma.Infrastructure.Llm.ModelEndpointOptions.SupportsThinking</c>'s
/// doc comment for the full reasoning, kept in sync by hand across both
/// minimal copies).
/// </summary>
public sealed class ChatModelOptions
{
    public const string SectionName = "Models:BaseModel";

    public int? ContextSize { get; set; }

    public bool SupportsThinking { get; set; }
}
