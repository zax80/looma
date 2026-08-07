using System.ComponentModel;
using Looma.Core.Abstractions;
using ModelContextProtocol.Server;

namespace Looma.MCP.Server.Tools;

/// <summary>Wraps <see cref="IAnswerCache.ClearAsync"/> — same operation as the CLI's <c>clear-cache</c> command.</summary>
[McpServerToolType]
public static class ClearCacheTool
{
    [McpServerTool(Name = "looma_clear_cache", ReadOnly = false, Destructive = true, Idempotent = true, OpenWorld = false)]
    [Description("Wipes Looma's answer cache (both the exact-match and semantic layers). Use after a prompt, model, or config change that a re-index wouldn't otherwise invalidate.")]
    public static async Task<string> ClearCache(
        IAnswerCache answerCache,
        CancellationToken cancellationToken = default)
    {
        await answerCache.ClearAsync(cancellationToken).ConfigureAwait(false);
        return "Answer cache cleared.";
    }
}
