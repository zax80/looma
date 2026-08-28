using System.Text.Json;
using Looma.Application.UseCases;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace Looma.MCP.Client;

/// <summary>
/// Remote (McpClient-mode) implementation of <see cref="IChatModelCapabilities"/>
/// — calls the <c>looma_capabilities</c> tool once and caches the result for
/// the lifetime of this singleton, since the answer (whether the remote
/// server's configured <c>BaseModel</c> supports reasoning) can't change
/// without restarting the server anyway. See
/// <see cref="IChatModelCapabilities"/>'s own doc comment for why a caller
/// like <c>Looma.UI.MAUI.Desktop</c> needs this remote round trip at all in
/// this mode, unlike the local <c>ChatModelCapabilities</c> Standalone mode
/// uses.
/// </summary>
public sealed class RemoteChatModelCapabilities : IChatModelCapabilities
{
    private readonly McpClient _client;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private bool? _cachedSupportsThinking;

    public RemoteChatModelCapabilities(McpClient client)
    {
        _client = client;
    }

    public async Task<bool> SupportsThinkingAsync(CancellationToken cancellationToken = default)
    {
        if (_cachedSupportsThinking is { } cached)
        {
            return cached;
        }

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Re-check after acquiring the lock — another caller may have
            // already populated it while this one was waiting.
            if (_cachedSupportsThinking is { } cachedAfterLock)
            {
                return cachedAfterLock;
            }

            var result = await _client
                .CallToolAsync("looma_capabilities", new Dictionary<string, object?>(), cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (result.IsError == true)
            {
                throw new McpException($"looma_capabilities returned an error: {RemoteStreamHelper.ExtractText(result) ?? "(no message)"}");
            }

            var text = RemoteStreamHelper.ExtractText(result)
                ?? throw new McpException("looma_capabilities returned no content.");

            using var document = JsonDocument.Parse(text);
            var supportsThinking = document.RootElement.TryGetProperty("supportsThinking", out var value) && value.GetBoolean();

            _cachedSupportsThinking = supportsThinking;
            return supportsThinking;
        }
        finally
        {
            _lock.Release();
        }
    }
}
