using System.ComponentModel;
using System.Text.Json;
using Looma.Application.UseCases;
using ModelContextProtocol.Server;

namespace Looma.MCP.Server.Tools;

/// <summary>
/// Wraps <see cref="IImageCaptionUseCase"/> — ad-hoc vision captioning for
/// one chat image attachment, not indexing. A single call, no streaming
/// needed, same reasoning as <see cref="TranscriptionTool"/>.
///
/// The image travels as a base64 string (<c>imageBase64</c>), same
/// approach as <see cref="TranscriptionTool"/>'s audio. The result
/// (<c>ImageCaptionResult</c> — caption + optional OCR text) is returned
/// as JSON via <see cref="Wire"/>, the same "serialize the real
/// Looma.Core.Entities record" convention every other tool here uses, so
/// Looma.MCP.Client deserializes straight back into it rather than
/// re-parsing a formatted sentence.
/// </summary>
[McpServerToolType]
public static class ImageCaptionTool
{
    [McpServerTool(Name = "looma_caption_image", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Captions a single image (base64-encoded) and extracts any visible text via OCR. Ad-hoc — not indexed, just returns the description. Result is JSON: {\"Caption\": \"...\", \"OcrText\": \"...\"|null}.")]
    public static async Task<string> CaptionImage(
        IImageCaptionUseCase imageCaptionUseCase,
        [Description("Base64-encoded image bytes.")] string imageBase64,
        CancellationToken cancellationToken = default)
    {
        byte[] imageBytes;
        try
        {
            imageBytes = Convert.FromBase64String(imageBase64);
        }
        catch (FormatException ex)
        {
            throw new ModelContextProtocol.McpException($"imageBase64 isn't valid base64: {ex.Message}");
        }

        await using var stream = new MemoryStream(imageBytes);
        var result = await imageCaptionUseCase.CaptionAsync(stream, cancellationToken).ConfigureAwait(false);

        return JsonSerializer.Serialize(result, Wire.Options);
    }
}
