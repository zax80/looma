using Looma.Application.UseCases;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace Looma.MCP.Client;

/// <summary>
/// Remote (McpClient-mode) implementation — calls the
/// <c>looma_extract_document</c> tool on a remote Looma.MCP.Server. The
/// whole document is read into memory and base64-encoded up front
/// (single-shot call, no streaming), same reasoning as
/// <see cref="RemoteTranscriptionUseCase"/>/<see cref="RemoteImageCaptionUseCase"/>.
/// </summary>
public sealed class RemoteDocumentExtractionUseCase : IDocumentExtractionUseCase
{
    private readonly McpClient _client;

    public RemoteDocumentExtractionUseCase(McpClient client)
    {
        _client = client;
    }

    public async Task<string> ExtractAsync(Stream documentStream, string fileName, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await documentStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var documentBase64 = Convert.ToBase64String(buffer.ToArray());

        var arguments = new Dictionary<string, object?>
        {
            ["documentBase64"] = documentBase64,
            ["fileName"] = fileName
        };

        var result = await _client.CallToolAsync("looma_extract_document", arguments, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.IsError == true)
        {
            throw new McpException($"looma_extract_document returned an error: {RemoteStreamHelper.ExtractText(result) ?? "(no message)"}");
        }

        return RemoteStreamHelper.ExtractText(result) ?? string.Empty;
    }
}
