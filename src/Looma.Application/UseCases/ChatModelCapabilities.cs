using Looma.Application.Configuration;
using Microsoft.Extensions.Options;

namespace Looma.Application.UseCases;

/// <summary>
/// Local (Standalone mode, and <c>Looma.MCP.Server</c> itself) implementation
/// of <see cref="IChatModelCapabilities"/> — a trivial synchronous read of
/// already-bound config wrapped in <see cref="Task.FromResult{TResult}"/>
/// to satisfy the interface's async shape, which exists for
/// <c>Looma.MCP.Client.RemoteChatModelCapabilities</c>'s benefit, not
/// this one's.
/// </summary>
public sealed class ChatModelCapabilities : IChatModelCapabilities
{
    private readonly ChatModelOptions _options;

    public ChatModelCapabilities(IOptions<ChatModelOptions> options)
    {
        _options = options.Value;
    }

    public Task<bool> SupportsThinkingAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_options.SupportsThinking);
}
