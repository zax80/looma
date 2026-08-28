using System.ComponentModel;
using System.Text.Json;
using Looma.Application.UseCases;
using ModelContextProtocol.Server;

namespace Looma.MCP.Server.Tools;

/// <summary>
/// Wraps <see cref="IChatModelCapabilities"/>. A single fast call, no
/// streaming — exists specifically so a remote caller (McpClient-mode
/// <c>Looma.CLI</c>, and <c>Looma.UI.MAUI.Desktop</c> in McpClient mode)
/// can decide whether to offer the "Ask"/"Think" toggle at all before ever
/// sending an actual <c>looma_answer</c>/<c>looma_chat</c> request — see
/// <see cref="IChatModelCapabilities"/>'s own doc comment for why this
/// can't just be a local config read in that mode.
/// </summary>
[McpServerToolType]
public static class CapabilitiesTool
{
    [McpServerTool(Name = "looma_capabilities", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Reports which optional generation capabilities the configured BaseModel supports — currently just whether it can be asked to reason (\"think\") before answering. Returns a small JSON object, e.g. {\"supportsThinking\":true}.")]
    public static async Task<string> GetCapabilities(
        IChatModelCapabilities capabilities,
        CancellationToken cancellationToken = default)
    {
        var supportsThinking = await capabilities.SupportsThinkingAsync(cancellationToken).ConfigureAwait(false);

        return JsonSerializer.Serialize(new { supportsThinking });
    }
}
