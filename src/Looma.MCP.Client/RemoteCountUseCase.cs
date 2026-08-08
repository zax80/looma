using System.Globalization;
using Looma.Application.UseCases;
using Looma.Core.Abstractions;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace Looma.MCP.Client;

/// <summary>Remote (MCP-client-mode) implementation — calls the <c>looma_count</c> tool on a remote Looma.MCP.Server.</summary>
public sealed class RemoteCountUseCase : ICountUseCase
{
    private readonly McpClient _client;

    public RemoteCountUseCase(McpClient client)
    {
        _client = client;
    }

    public async Task<long> CountAsync(VectorCollection collection, CancellationToken cancellationToken = default)
    {
        var arguments = new Dictionary<string, object?>
        {
            ["collection"] = collection == VectorCollection.Images ? "images" : "documents"
        };

        var result = await _client.CallToolAsync("looma_count", arguments, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.IsError == true)
        {
            throw new McpException($"looma_count returned an error: {RemoteStreamHelper.ExtractText(result) ?? "(no message)"}");
        }

        var text = RemoteStreamHelper.ExtractText(result)
            ?? throw new McpException("looma_count returned no content.");

        return long.Parse(text, CultureInfo.InvariantCulture);
    }
}
