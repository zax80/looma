using Looma.Core.Abstractions;
using Looma.Core.Entities;
using Microsoft.Extensions.AI;

namespace Looma.Infrastructure.Llm.Vision;

/// <summary>
/// Captioning + OCR in one model call, against <c>Models.VisionModel</c>
/// (Qwen2.5-VL by default) via Ollama's OpenAI-compatible endpoint. Takes a
/// dedicated <see cref="IChatClient"/> instance (keyed "vision" in DI, see
/// <c>ServiceCollectionExtensions.AddLoomaImageCaptioner</c>) rather than the
/// app's general chat client — a different model/endpoint than
/// <c>Models.BaseModel</c>.
/// </summary>
public sealed class OllamaImageCaptioner : IImageCaptioner
{
    /// <summary>
    /// Structured plain-text format rather than asking for JSON: small local
    /// vision models are noticeably less reliable at strict JSON than at a
    /// simple labeled-line format, and this only has two fields to parse.
    /// </summary>
    private const string Prompt =
        "Look at this image and respond in exactly this two-line format, nothing else:\n" +
        "Caption: <a few sentences describing what's in the image>\n" +
        "Text: <any legible text visible in the image, transcribed verbatim, or \"none\" if there isn't any>";

    private readonly IChatClient _chatClient;

    public OllamaImageCaptioner(IChatClient chatClient)
    {
        _chatClient = chatClient;
    }

    public async Task<ImageCaptionResult> CaptionAsync(Stream imageStream, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await imageStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var bytes = buffer.ToArray();

        // Extension isn't available from a bare Stream, so this only has the
        // bytes to sniff from — real image bytes always match one of the two
        // magic-byte signatures, so the extension fallback in the sniffer is
        // effectively unreachable here, but kept for defense in depth.
        var mediaType = ImageMediaTypeSniffer.Detect(bytes, fallbackExtension: ".png");

        var message = new ChatMessage(ChatRole.User,
        [
            new TextContent(Prompt),
            new DataContent(bytes, mediaType)
        ]);

        var response = await _chatClient.GetResponseAsync([message], cancellationToken: cancellationToken).ConfigureAwait(false);
        return ParseResponse(response.Text);
    }

    /// <summary>
    /// Line-based, not index-slicing — a small local VLM won't always hit
    /// the exact requested format, and a caption that happens to contain the
    /// word "text" shouldn't be able to confuse a naive substring split.
    /// Lenient fallback: if neither label is found at all, the whole
    /// response becomes the caption rather than being dropped.
    /// </summary>
    public static ImageCaptionResult ParseResponse(string responseText)
    {
        var text = responseText?.Trim() ?? string.Empty;
        var captionLines = new List<string>();
        var ocrLines = new List<string>();
        var mode = 0; // 0 = before any label, 1 = accumulating caption, 2 = accumulating OCR text

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.TrimStart().StartsWith("Caption:", StringComparison.OrdinalIgnoreCase))
            {
                mode = 1;
                captionLines.Add(StripLabel(line, "Caption:"));
            }
            else if (line.TrimStart().StartsWith("Text:", StringComparison.OrdinalIgnoreCase))
            {
                mode = 2;
                ocrLines.Add(StripLabel(line, "Text:"));
            }
            else if (mode == 1)
            {
                captionLines.Add(line);
            }
            else if (mode == 2)
            {
                ocrLines.Add(line);
            }
        }

        var caption = string.Join('\n', captionLines).Trim();
        var ocr = string.Join('\n', ocrLines).Trim();
        var noOcr = string.IsNullOrWhiteSpace(ocr) || string.Equals(ocr, "none", StringComparison.OrdinalIgnoreCase);

        return new ImageCaptionResult
        {
            Caption = string.IsNullOrWhiteSpace(caption) ? text : caption,
            OcrText = noOcr ? null : ocr
        };
    }

    private static string StripLabel(string line, string label)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith(label, StringComparison.OrdinalIgnoreCase)
            ? trimmed[label.Length..].Trim()
            : trimmed.Trim();
    }
}
