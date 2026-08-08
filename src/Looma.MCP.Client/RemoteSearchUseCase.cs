using Looma.Application.UseCases;
using Looma.Core.Abstractions;
using Looma.Core.Entities;
using ModelContextProtocol.Client;

namespace Looma.MCP.Client;

/// <summary>Remote (MCP-client-mode) implementation — calls the <c>looma_search</c> tool on a remote Looma.MCP.Server.</summary>
public sealed class RemoteSearchUseCase : ISearchUseCase
{
    private readonly McpClient _client;

    public RemoteSearchUseCase(McpClient client)
    {
        _client = client;
    }

    public IAsyncEnumerable<VectorSearchResult> SearchAsync(
        string query,
        VectorCollection collection = VectorCollection.Documents,
        int topK = 5,
        float? minRelevanceScore = null,
        CancellationToken cancellationToken = default)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["query"] = query,
            ["collection"] = collection == VectorCollection.Images ? "images" : "documents",
            ["topK"] = topK,
            ["minRelevanceScore"] = minRelevanceScore
        };

        return RemoteStreamHelper.StreamAsync<VectorSearchResult>(_client, "looma_search", arguments, cancellationToken);
    }
}
