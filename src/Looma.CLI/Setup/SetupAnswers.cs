namespace Looma.CLI.Setup;

/// <summary>
/// The wizard's collected answers — deliberately just these five, matching
/// exactly what was scoped for the CLI-first setup wizard: domain, location,
/// deployment mode, and (for McpClient) the server endpoint, plus the
/// horizontal-scale toggle. Everything else in config.json stays whatever
/// it already was (or the shipped default for a brand-new file) — anything
/// not covered here is "advanced settings": edit config.json directly, see
/// docs/config-reference.md.
/// </summary>
public sealed record SetupAnswers(
    string DomainName,
    string Location,
    string DeploymentMode,
    string? McpServerEndpoint,
    bool HorizontalScale);
