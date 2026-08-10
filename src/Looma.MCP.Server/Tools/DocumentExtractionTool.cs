using System.ComponentModel;
using Looma.Application.UseCases;
using ModelContextProtocol.Server;

namespace Looma.MCP.Server.Tools;

/// <summary>
/// Wraps <see cref="IDocumentExtractionUseCase"/> — ad-hoc text extraction
/// for one chat document attachment, not indexing. A single call, no
/// streaming needed, same reasoning as <see cref="TranscriptionTool"/> and
/// <see cref="ImageCaptionTool"/>.
///
/// The document travels as a base64 string (<c>documentBase64</c>) plus
/// its original <c>fileName</c> (used only to pick the right extractor by
/// extension — see <see cref="DocumentExtractionUseCase"/>), same approach
/// as the other ad-hoc attachment tools.
/// </summary>
[McpServerToolType]
public static class DocumentExtractionTool
{
    [McpServerTool(Name = "looma_extract_document", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Extracts plain text from a single attached document (base64-encoded .txt/.md/.csv/.pdf/.docx/.xlsx). Ad-hoc — not indexed, just returns the text.")]
    public static async Task<string> ExtractDocument(
        IDocumentExtractionUseCase documentExtractionUseCase,
        [Description("Base64-encoded document bytes.")] string documentBase64,
        [Description("Original file name, used only to determine the format by extension (.txt/.md/.csv/.pdf/.docx/.xlsx).")] string fileName,
        CancellationToken cancellationToken = default)
    {
        byte[] documentBytes;
        try
        {
            documentBytes = Convert.FromBase64String(documentBase64);
        }
        catch (FormatException ex)
        {
            throw new ModelContextProtocol.McpException($"documentBase64 isn't valid base64: {ex.Message}");
        }

        await using var stream = new MemoryStream(documentBytes);
        return await documentExtractionUseCase.ExtractAsync(stream, fileName, cancellationToken).ConfigureAwait(false);
    }
}
