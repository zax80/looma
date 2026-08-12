using System.ComponentModel;
using Looma.Application.UseCases;
using Looma.Core.Exceptions;
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

        long count;
        try
        {
            count = await countUseCase.CountAsync(parsedCollection, cancellationToken).ConfigureAwait(false);
        }
        catch (VectorStoreUnavailableException ex)
        {
            throw ToolErrorTranslation.Translate(ex);
        }

        // Plain number, not "collection: N" — keeps this trivially parseable
        // for a machine client (Looma.MCP.Client); the tool name and
        // `collection` argument already carry which collection was queried.
        return count.ToString();
    }
}
