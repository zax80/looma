using System.ComponentModel;
using Looma.Application.UseCases;
using ModelContextProtocol.Server;

namespace Looma.MCP.Server.Tools;

/// <summary>Wraps <see cref="ICountUseCase"/>. A single fast call — no streaming needed.</summary>
[McpServerToolType]
public static class CountTool
{
    [McpServerTool(Name = "looma_count", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Returns how many chunks are currently stored in a Looma vector collection.")]
    public static async Task<string> Count(
        ICountUseCase countUseCase,
        [Description("\"documents\" or \"images\". Defaults to \"documents\".")] string collection = "documents",
        CancellationToken cancellationToken = default)
    {
        var parsedCollection = VectorCollectionParser.Parse(collection);
        var count = await countUseCase.CountAsync(parsedCollection, cancellationToken).ConfigureAwait(false);
        return $"{collection}: {count}";
    }
}
