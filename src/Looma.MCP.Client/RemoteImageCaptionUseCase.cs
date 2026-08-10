using System.Text.Json;
using Looma.Application.UseCases;
using Looma.Core.Entities;
using ModelContextProtocol;
using ModelContextProtocol.Client;

namespace Looma.MCP.Client;

/// <summary>
/// Remote (McpClient-mode) implementation — calls the
/// <c>looma_caption_image</c> tool on a remote Looma.MCP.Server. The whole
/// image is read into memory and base64-encoded up front (single-shot
/// call, no streaming), same reasoning as
/// <see cref="RemoteTranscriptionUseCase"/>.
/// </summary>
public sealed class RemoteImageCaptionUseCase : IImageCaptionUseCase
{
    private readonly McpClient _client;

    public RemoteImageCaptionUseCase(McpClient client)
    {
        _client = client;
    }

    public async Task<ImageCaptionResult> CaptionAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await imageStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var imageBase64 = Convert.ToBase64String(buffer.ToArray());

        var arguments = new Dictionary<string, object?> { ["imageBase64"] = imageBase64 };

        var result = await _client.CallToolAsync("looma_caption_image", arguments, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (result.IsError == true)
        {
            throw new McpException($"looma_caption_image returned an error: {RemoteStreamHelper.ExtractText(result) ?? "(no message)"}");
        }

        var json = RemoteStreamHelper.ExtractText(result)
            ?? throw new McpException("looma_caption_image returned no content.");

        return JsonSerializer.Deserialize<ImageCaptionResult>(json, Wire.Options)
            ?? throw new McpException("looma_caption_image returned unparseable JSON.");
    }
}
