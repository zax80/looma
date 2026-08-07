namespace Looma.Infrastructure.VectorStore.Qdrant;

/// <summary>
/// Binds to the <c>VectorStore</c> section of config.json. Matches the
/// shape documented in the project brief — see
/// <c>docs/looma-project-brief.md</c> section 8 for the reference file.
/// </summary>
public sealed class QdrantOptions
{
    public const string SectionName = "VectorStore";

    public string Endpoint { get; set; } = "http://localhost:6333";

    /// <summary>Qdrant API-key auth. Null only acceptable for a local, network-isolated instance.</summary>
    public string? ApiKey { get; set; }

    public QdrantCollectionNames Collections { get; set; } = new();
}

public sealed class QdrantCollectionNames
{
    public string Documents { get; set; } = "documents";
    public string Images { get; set; } = "images";
}
