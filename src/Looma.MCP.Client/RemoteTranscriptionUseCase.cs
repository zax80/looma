using Looma.Application.UseCases;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace Looma.MCP.Client;

/// <summary>
/// Remote (McpClient-mode) implementation — calls the <c>looma_transcribe</c>
/// tool on a remote Looma.MCP.Server. The whole audio stream is read into
/// memory and base64-encoded up front (single-shot call, no streaming) —
/// fine for a short chat voice-input clip, same reasoning as the tool
/// itself.
/// </summary>
public sealed class RemoteTranscriptionUseCase : ITranscriptionUseCase
{
    private readonly McpClient _client;

    public RemoteTranscriptionUseCase(McpClient client)
    {
        _client = client;
    }

    public async Task<string> TranscribeAsync(Stream audioStream, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await audioStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var audioBase64 = Convert.ToBase64String(buffer.ToArray());

        var arguments = new Dictionary<string, object?> { ["audioBase64"] = audioBase64 };

        var result = await _client.CallToolAsync("looma_transcribe", arguments, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.IsError == true)
        {
            throw new McpException($"looma_transcribe returned an error: {RemoteStreamHelper.ExtractText(result) ?? "(no message)"}");
        }

        return RemoteStreamHelper.ExtractText(result) ?? string.Empty;
    }
}
