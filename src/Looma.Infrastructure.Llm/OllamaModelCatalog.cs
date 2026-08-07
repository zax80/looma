namespace Looma.Infrastructure.Llm;

/// <summary>
/// Pure diffing logic used by <see cref="OllamaLifecycleManager"/> — kept
/// separate from the HTTP/process-management code specifically so it can be
/// unit-tested without a real Ollama instance.
/// </summary>
public static class OllamaModelCatalog
{
    /// <summary>
    /// Which of <paramref name="requiredModels"/> aren't present in
    /// <paramref name="installedModels"/>, matched case-insensitively (Ollama
    /// model names are case-preserving but comparisons in practice are not
    /// case-sensitive) and tag-normalized (see <see cref="NormalizeTag"/>).
    /// Blank entries in <paramref name="requiredModels"/> are ignored — an
    /// unset model in config.json isn't "missing", it's just not configured
    /// for use yet.
    /// </summary>
    public static IReadOnlyList<string> FindMissing(IReadOnlyList<string> requiredModels, IReadOnlyList<string> installedModels)
    {
        var installedSet = new HashSet<string>(installedModels.Select(NormalizeTag), StringComparer.OrdinalIgnoreCase);

        return requiredModels
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Select(m => m.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(m => !installedSet.Contains(NormalizeTag(m)))
            .ToList();
    }

    /// <summary>
    /// Ollama resolves a tag-less model reference to <c>:latest</c> implicitly
    /// — pulling "nomic-embed-text" really pulls "nomic-embed-text:latest",
    /// and <c>/api/tags</c> reports it back with that tag attached. Without
    /// normalizing this, a tag-less name in config.json never string-matches
    /// what <c>/api/tags</c> reports, so it's treated as "missing" — and
    /// re-pulled (a real manifest round trip + hash verification against the
    /// registry) — on every single invocation, not just when it's actually absent.
    /// </summary>
    private static string NormalizeTag(string modelName) =>
        modelName.Contains(':') ? modelName : $"{modelName}:latest";
}
