using System.Text.Json;
using System.Text.Json.Nodes;

namespace Looma.CLI.Setup;

/// <summary>
/// The interactive half of <c>looma setup</c> — prompts for whatever wasn't
/// already given as a flag (see <c>Looma.CLI.Commands.SetupCommand</c>),
/// then hands off to the pure/testable pieces (<see cref="ModelRecommendationCatalog"/>,
/// <see cref="ConfigMerger"/>, <see cref="DefaultConfigTemplate"/>) to
/// actually build and write config.json. Never prompts when input is
/// redirected (CI, piped input) — same "don't hang, just use the default"
/// stance as <c>Program.ConfirmInstallAsync</c>; every prompt has a safe
/// fallback so a fully non-interactive run with zero flags still produces a
/// working config.json.
/// </summary>
public static class SetupWizard
{
    /// <summary>
    /// The only two values <c>Program.Main</c>'s own dispatch actually
    /// understands for <c>Deployment:Mode</c> — see its switch statement.
    /// "MCP server with clients" (the third shape from the original wizard
    /// requirement) isn't a third value of this same field: it's Standalone
    /// running as <c>Looma.MCP.Server</c> instead of <c>Looma.CLI</c> on
    /// one machine (that project doesn't read <c>Deployment:Mode</c> at
    /// all — it always serves), with every other machine's config.json set
    /// to <c>McpClient</c> pointing at it. Running this wizard on the
    /// server machine and picking <c>Standalone</c> is correct even though
    /// that machine will actually run <c>Looma.MCP.Server</c>, not
    /// <c>Looma.CLI</c> — the config shape is identical either way.
    /// </summary>
    private static readonly string[] ValidModes = ["Standalone", "McpClient"];

    public static async Task<int> RunAsync(
        string configPath,
        string? domain,
        string? location,
        string? mode,
        string? mcpEndpoint,
        bool? horizontalScale)
    {
        var existingRoot = File.Exists(configPath)
            ? JsonNode.Parse(await File.ReadAllTextAsync(configPath)) as JsonObject
            : null;

        var interactive = !Console.IsInputRedirected;

        domain ??= Prompt(
            interactive,
            "Domain name (a label; also picks a quality/speed tier for the base model)",
            TryGetString(existingRoot, "DomainName") ?? "Looma");

        location ??= Prompt(
            interactive,
            "Geographic location (picks the spoken language + a region-tuned model if one exists)",
            TryGetString(existingRoot, "Models", "SpeechToTextModel", "Language") ?? string.Empty);

        mode ??= PromptChoice(
            interactive,
            "Deployment mode",
            ValidModes,
            TryGetString(existingRoot, "Deployment", "Mode") ?? "Standalone");

        var matchedMode = ValidModes.FirstOrDefault(m => string.Equals(m, mode, StringComparison.OrdinalIgnoreCase));
        if (matchedMode is null)
        {
            Console.Error.WriteLine($"Unknown deployment mode '{mode}'. Expected one of: {string.Join(", ", ValidModes)}.");
            return 1;
        }

        mode = matchedMode;

        if (mode == "McpClient")
        {
            mcpEndpoint ??= Prompt(
                interactive,
                "Remote MCP server endpoint",
                TryGetString(existingRoot, "Deployment", "McpServerEndpoint") ?? "https://localhost:3001");
        }

        horizontalScale ??= PromptYesNo(
            interactive,
            "Prepare for real horizontal scaling (multiple MCP.Server instances behind a shared queue/cache)?",
            TryGetBool(existingRoot, "Redis", "Enabled") ?? false);

        var answers = new SetupAnswers(domain, location, mode, mcpEndpoint, horizontalScale.Value);
        var recommendation = ModelRecommendationCatalog.Recommend(domain, location);

        var root = existingRoot ?? DefaultConfigTemplate.Build();
        ConfigMerger.Apply(root, answers, recommendation);

        await File.WriteAllTextAsync(configPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        PrintSummary(configPath, answers, recommendation);
        return 0;
    }

    private static void PrintSummary(string configPath, SetupAnswers answers, ModelRecommendation recommendation)
    {
        Console.WriteLine();
        Console.WriteLine($"Wrote {configPath}:");
        Console.WriteLine($"  Domain: {answers.DomainName}");
        Console.WriteLine($"  Location: {(string.IsNullOrWhiteSpace(answers.Location) ? "(none)" : answers.Location)}");
        Console.WriteLine(
            $"  Deployment mode: {answers.DeploymentMode}"
            + (answers.DeploymentMode == "McpClient" ? $" -> {answers.McpServerEndpoint}" : string.Empty));
        Console.WriteLine($"  Horizontal scale prepared: {answers.HorizontalScale}");
        Console.WriteLine($"  Base model: {recommendation.BaseModel} (SupportsThinking: {recommendation.SupportsThinking})");
        Console.WriteLine($"  Vision model: {recommendation.VisionModel}");
        Console.WriteLine($"  Embedding model: {recommendation.EmbeddingModel}");
        Console.WriteLine($"  Whisper language: {recommendation.WhisperLanguage}");
        Console.WriteLine($"  ({recommendation.Note})");
        Console.WriteLine();
        Console.WriteLine("Anything not covered here (RAG tuning, TLS, cache sizes, etc.) is \"advanced");
        Console.WriteLine("settings\" — edit config.json directly; see docs/config-reference.md for every field.");

        if (answers.HorizontalScale)
        {
            Console.WriteLine();
            Console.WriteLine("Note: Redis.Enabled is now true, but the actual horizontal-scaling backend");
            Console.WriteLine("(job queue / pub-sub / shared cache) isn't implemented yet — this only records");
            Console.WriteLine("the intent in config for when it is.");
        }
    }

    /// <summary>
    /// Walks a dotted path through a possibly-hand-edited config.json and
    /// reads a string at the end of it, returning <c>null</c> instead of
    /// throwing for any of: a missing intermediate section, an intermediate
    /// value that exists but isn't actually an object (e.g. someone set
    /// <c>"Deployment": "Standalone"</c> instead of an object), or a final
    /// value that isn't actually a string. <c>looma setup</c> explicitly
    /// promises to work against an already-existing config — a malformed
    /// one should fall back to defaults, not crash the wizard.
    /// </summary>
    private static string? TryGetString(JsonObject? root, params string[] path) => TryGetValue<string>(root, path);

    private static bool? TryGetBool(JsonObject? root, params string[] path) => TryGetValue<bool>(root, path);

    private static T? TryGetValue<T>(JsonObject? root, string[] path)
    {
        JsonNode? current = root;
        foreach (var segment in path)
        {
            if (current is not JsonObject currentObject)
            {
                return default;
            }

            current = currentObject[segment];
        }

        if (current is null)
        {
            return default;
        }

        try
        {
            return current.GetValue<T>();
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            return default;
        }
    }

    private static string Prompt(bool interactive, string label, string defaultValue)
    {
        if (!interactive)
        {
            return defaultValue;
        }

        Console.Write(string.IsNullOrEmpty(defaultValue) ? $"{label}: " : $"{label} [{defaultValue}]: ");
        var input = Console.ReadLine();
        return string.IsNullOrWhiteSpace(input) ? defaultValue : input.Trim();
    }

    private static string PromptChoice(bool interactive, string label, string[] choices, string defaultValue)
    {
        if (!interactive)
        {
            return defaultValue;
        }

        Console.Write($"{label} ({string.Join("/", choices)}) [{defaultValue}]: ");
        var input = Console.ReadLine();
        return string.IsNullOrWhiteSpace(input) ? defaultValue : input.Trim();
    }

    private static bool PromptYesNo(bool interactive, string label, bool defaultValue)
    {
        if (!interactive)
        {
            return defaultValue;
        }

        Console.Write($"{label} [{(defaultValue ? "Y/n" : "y/N")}]: ");
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return defaultValue;
        }

        return string.Equals(input, "y", StringComparison.OrdinalIgnoreCase)
            || string.Equals(input, "yes", StringComparison.OrdinalIgnoreCase);
    }
}
