using Looma.Core.Abstractions;
using Looma.Core.Entities;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace Looma.MCP.Client;

/// <summary>
/// Remote (MCP-client-mode) implementation of <see cref="IAnswerCache"/>.
/// Only <see cref="ClearAsync"/> is real — it calls the remote
/// <c>looma_clear_cache</c> tool, the same thing the CLI's <c>clear-cache</c>
/// command calls <see cref="IAnswerCache.ClearAsync"/> for in standalone
/// mode. The other three methods exist only because a real
/// <c>AnswerUseCase</c> (standalone mode) calls them directly for
/// lookup/store — in MCP-client mode, <see cref="RemoteAnswerUseCase"/>
/// replaces that whole use case, and the remote <c>looma_answer</c> tool
/// does its own caching entirely server-side, so nothing in this process
/// ever calls them. They throw rather than silently no-op, so a future bug
/// that did start calling them here would fail loudly instead of quietly
/// behaving as if caching were disabled.
/// </summary>
public sealed class RemoteAnswerCache : IAnswerCache
{
    private const string NotSupportedMessage =
        "Not used in MCP-client mode — the remote looma_answer tool handles caching entirely server-side.";

    private readonly McpClient _client;

    public RemoteAnswerCache(McpClient client)
    {
        _client = client;
    }

    public Task<CachedAnswer?> TryGetExactAsync(string question, long documentsVersion, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(NotSupportedMessage);

    public Task<CachedAnswer?> TryGetSemanticAsync(ReadOnlyMemory<float> questionEmbedding, long documentsVersion, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(NotSupportedMessage);

    public Task StoreAsync(string question, ReadOnlyMemory<float> questionEmbedding, string answerText, IReadOnlyList<DocumentChunk> citations, long documentsVersion, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(NotSupportedMessage);

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        var result = await _client.CallToolAsync("looma_clear_cache", cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.IsError == true)
        {
            throw new McpException($"looma_clear_cache returned an error: {RemoteStreamHelper.ExtractText(result) ?? "(no message)"}");
        }
    }
}
