namespace Looma.Infrastructure.Llm;

/// <summary>Binds to the <c>Security</c> section of config.json.</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    public IReadOnlyList<string> AllowedInferenceHosts { get; set; } = [];

    /// <summary>
    /// When true (the documented default), any configured inference endpoint
    /// whose host isn't in <see cref="AllowedInferenceHosts"/> fails DI
    /// registration outright. Setting this false is an explicit, auditable
    /// opt-out — not something a bad default should silently allow.
    /// </summary>
    public bool BlockNonLocalEndpoints { get; set; } = true;
}
