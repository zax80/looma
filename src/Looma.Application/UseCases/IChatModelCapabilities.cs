namespace Looma.Application.UseCases;

/// <summary>
/// Whether the configured <c>BaseModel</c> supports being asked to reason
/// ("think") before answering — see
/// <see cref="Core.Entities.ThinkingMode"/>'s doc comment. Its own
/// abstraction, rather than a caller just reading
/// <c>Configuration.ChatModelOptions.SupportsThinking</c> directly, because
/// a caller like <c>Looma.UI.MAUI.Desktop</c> must work identically in both
/// Standalone mode (config.json is local — the answer is just that flag,
/// no I/O) and McpClient mode (config.json's <c>Models</c> section lives on
/// the remote server, so answering needs one round trip — see
/// <c>Looma.MCP.Client.RemoteChatModelCapabilities</c>). Same
/// local-vs-remote-implementation shape as <c>IAnswerCache</c>/<c>IChatUseCase</c>
/// elsewhere in this codebase; the local implementation
/// (<see cref="ChatModelCapabilities"/>) is registered by
/// <see cref="ServiceCollectionExtensions.AddLoomaApplicationUseCases"/>,
/// used directly by <c>Looma.MCP.Server</c> too (a <c>looma_capabilities</c>
/// tool wraps it for remote callers).
/// </summary>
public interface IChatModelCapabilities
{
    Task<bool> SupportsThinkingAsync(CancellationToken cancellationToken = default);
}
