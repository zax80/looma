using Looma.Application.UseCases;
using Looma.Core.Entities;
using ModelContextProtocol.Client;

namespace Looma.MCP.Client;

/// <summary>
/// Remote (MCP-client-mode) implementation — calls the <c>looma_answer</c>
/// tool on a remote Looma.MCP.Server. Citations arriving over the wire have
/// their <see cref="DocumentChunk.Embedding"/> stripped (the server never
/// sends it — see <c>Looma.MCP.Server.Tools.AnswerTool</c>'s doc comment);
/// callers that only display citations are unaffected, but nothing here
/// should assume a populated embedding on a citation chunk.
/// </summary>
public sealed class RemoteAnswerUseCase : IAnswerUseCase
{
    private readonly McpClient _client;

    public RemoteAnswerUseCase(McpClient client)
    {
        _client = client;
    }

    public IAsyncEnumerable<AnswerToken> AnswerAsync(string question, CancellationToken cancellationToken = default)
    {
        var arguments = new Dictionary<string, object?> { ["question"] = question };

        return RemoteStreamHelper.StreamAsync<AnswerToken>(_client, "looma_answer", arguments, cancellationToken);
    }
}
