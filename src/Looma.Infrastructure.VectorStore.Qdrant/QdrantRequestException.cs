namespace Looma.Infrastructure.VectorStore.Qdrant;

/// <summary>Thrown when a request to Qdrant fails. Carries Qdrant's own error text; never document/chunk content.</summary>
public sealed class QdrantRequestException : Exception
{
    public QdrantRequestException(string message) : base(message)
    {
    }

    public QdrantRequestException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
