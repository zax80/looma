namespace Looma.Infrastructure.VectorStore.Qdrant;

/// <summary>
/// Pure normalization for the exact-match cache key — case and whitespace
/// differences shouldn't turn what's really the same question into a miss.
/// Kept as a standalone pure function so it's unit-testable without a real
/// file or Qdrant instance, matching this project's existing pattern for
/// pure logic (e.g. <c>OllamaModelCatalog.FindMissing</c>).
/// </summary>
public static class AnswerCacheQuestionNormalizer
{
    public static string Normalize(string question)
    {
        ArgumentNullException.ThrowIfNull(question);

        var trimmed = question.Trim();
        if (trimmed.Length == 0)
        {
            return string.Empty;
        }

        var words = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', words).ToLowerInvariant();
    }
}
