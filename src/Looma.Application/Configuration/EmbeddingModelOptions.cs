namespace Looma.Application.Configuration;

/// <summary>
/// Binds to the <c>Models:EmbeddingModel</c> section of config.json.
/// Deliberately a separate, minimal type from
/// <c>Looma.Infrastructure.Llm.LlmOptions</c> — Application must not
/// reference an Infrastructure.* project, so it declares just the one field
/// (vector dimensionality) it actually needs from that config section.
/// </summary>
public sealed class EmbeddingModelOptions
{
    public const string SectionName = "Models:EmbeddingModel";

    public int Dimensions { get; set; } = 768;
}
