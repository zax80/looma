namespace Looma.Application.Configuration;

/// <summary>
/// Binds to the <c>Models:BaseModel</c> section of config.json.
/// Deliberately a separate, minimal type from
/// <c>Looma.Infrastructure.Llm.LlmOptions</c> — same reasoning as
/// <see cref="EmbeddingModelOptions"/> (Application must not reference an
/// Infrastructure.* project) — it declares just the one field
/// <c>Looma.Application.Internal.ConversationHistoryBudget</c> actually
/// needs from that config section: the model's real context window, so
/// chat history sent to it can be bounded to what it can actually hold.
/// </summary>
public sealed class ChatModelOptions
{
    public const string SectionName = "Models:BaseModel";

    public int? ContextSize { get; set; }
}
