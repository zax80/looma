namespace Looma.CLI.Setup;

/// <summary>
/// Turns the wizard's free-text "domain" and "location" answers into a
/// concrete <see cref="ModelRecommendation"/>. Two independent axes, per
/// Ivan's own framing of the wizard requirements:
/// <list type="bullet">
/// <item><b>Location</b> picks the spoken language (feeds
/// <c>Models.SpeechToTextModel.Language</c>) and, for a handful of known
/// locations, a region-tuned chat model that outperforms a generic one for
/// that language — e.g. Bulgaria → BgGPT, already the default in
/// config.json and the one regional model this codebase has actually
/// exercised end-to-end.</item>
/// <item><b>Domain</b> picks a quality/speed tier — NOT a claim about
/// domain-specific model expertise (nothing here is trained on legal or
/// medical text specifically), just the same generic, defensible heuristic
/// the Ask/Think toggle is already built on: a domain where getting the
/// answer right matters more than getting it fast (legal, medical,
/// compliance, research) warrants a bigger model with reasoning enabled;
/// a high-volume, low-stakes domain (support, FAQ) warrants a smaller,
/// faster one; everything else gets a balanced default.</item>
/// </list>
/// Both axes are keyword-matched, case-insensitively, against free text —
/// deliberately loose (a fixed dropdown would force English category names
/// on every domain, which cuts against "DomainName" being a genuinely free
/// label). No match on either axis falls back to the generic default this
/// project already shipped with (docs/looma-project-brief.md section 8).
/// </summary>
public static class ModelRecommendationCatalog
{
    private const string GenericVisionModel = "qwen2.5vl:7b";
    private const string GenericEmbeddingModel = "nomic-embed-text";

    private sealed record LocationProfile(string[] Keywords, string Language, string? BaseModelOverride, bool? SupportsThinkingOverride);

    private sealed record DomainProfile(string[] Keywords, string Tier, string BaseModelTag, bool SupportsThinking);

    // Order matters: first match wins. Keep specific overrides before the
    // implicit "no match" fallback handled in Recommend() below.
    private static readonly LocationProfile[] Locations =
    [
        new(["bulgaria", "bulgarian", "bg", "sofia", "plovdiv"], "bg",
            "hf.co/INSAIT-Institute/BgGPT-Gemma-3-12B-IT-GGUF:Q4_K_M", SupportsThinkingOverride: false),
    ];

    private static readonly DomainProfile[] Domains =
    [
        new(["legal", "law", "compliance", "medical", "health", "clinical", "research", "finance", "audit"],
            "Careful (larger model, reasoning enabled)", "qwen3:14b", SupportsThinking: true),
        new(["support", "helpdesk", "faq", "customer service", "chat"],
            "Fast (smaller model, quick responses)", "qwen3:4b", SupportsThinking: false),
    ];

    private const string DefaultTier = "Standard (balanced default)";
    private const string DefaultBaseModelTag = "qwen3:8b";

    public static ModelRecommendation Recommend(string domain, string location)
    {
        var locationProfile = Locations.FirstOrDefault(p =>
            p.Keywords.Any(k => location.Contains(k, StringComparison.OrdinalIgnoreCase)));

        var domainProfile = Domains.FirstOrDefault(p =>
            p.Keywords.Any(k => domain.Contains(k, StringComparison.OrdinalIgnoreCase)));

        var tier = domainProfile?.Tier ?? DefaultTier;
        var baseModel = locationProfile?.BaseModelOverride ?? domainProfile?.BaseModelTag ?? DefaultBaseModelTag;
        var supportsThinking = locationProfile?.SupportsThinkingOverride ?? domainProfile?.SupportsThinking ?? false;
        var language = locationProfile?.Language ?? "auto";

        var noteParts = new List<string> { $"domain tier: {tier}" };
        noteParts.Add(locationProfile is not null
            ? $"location match: using a model tuned for '{language}'"
            : "location: no specific match, using the generic model + language auto-detect");

        return new ModelRecommendation(
            BaseModel: baseModel,
            VisionModel: GenericVisionModel,
            EmbeddingModel: GenericEmbeddingModel,
            WhisperLanguage: language,
            SupportsThinking: supportsThinking,
            Note: string.Join("; ", noteParts));
    }
}
