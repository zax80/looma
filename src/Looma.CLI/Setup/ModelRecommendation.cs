namespace Looma.CLI.Setup;

/// <summary>
/// The bundle of model defaults <see cref="ModelRecommendationCatalog"/>
/// suggests for a given (domain, location) pair — written into
/// <c>Models.*</c> by <see cref="SetupWizard"/>. These are starting points,
/// not guarantees: an Ollama tag that doesn't exist (or a typo, like the
/// real "qwen2.5-vl:7b" vs "qwen2.5vl:7b" mixup this codebase already hit
/// once) still fails loudly and specifically when Looma actually tries to
/// pull it — the wizard writing a bad tag isn't a silent failure mode, just
/// a wrong config value like any other, fixable in config.json afterward.
/// </summary>
public sealed record ModelRecommendation(
    string BaseModel,
    string VisionModel,
    string EmbeddingModel,
    string WhisperLanguage,
    bool SupportsThinking,
    string Note);
