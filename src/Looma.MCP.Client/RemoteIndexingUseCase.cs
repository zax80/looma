using Looma.Application.UseCases;
using Looma.Core.Entities;
using ModelContextProtocol.Client;

namespace Looma.MCP.Client;

/// <summary>Remote (MCP-client-mode) implementation — calls the <c>looma_index</c> tool on a remote Looma.MCP.Server.</summary>
public sealed class RemoteIndexingUseCase : IIndexingUseCase
{
    private readonly McpClient _client;

    public RemoteIndexingUseCase(McpClient client)
    {
        _client = client;
    }

    public IAsyncEnumerable<IndexingProgress> IndexAsync(
        string path,
        bool recursive = true,
        bool clearFirst = false,
        CancellationToken cancellationToken = default)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["path"] = path,
            ["recursive"] = recursive,
            ["clearFirst"] = clearFirst
        };

        return RemoteStreamHelper.StreamAsync<IndexingProgress>(_client, "looma_index", arguments, cancellationToken);
    }
}
