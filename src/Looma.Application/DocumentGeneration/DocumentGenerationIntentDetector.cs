using Looma.Core.Entities;

namespace Looma.Application.DocumentGeneration;

/// <summary>
/// Heuristic-only detection of "the user is asking for a document to be
/// generated" from their chat message text — e.g. "write this up as a
/// report", "generate a summary document", "export this as a .docx".
/// Deliberately keyword-based, not LLM-based: asking the model itself to
/// signal intent would compete with <c>ChatCompletionUseCase</c>'s strict
/// grounding system prompt (see its doc comment — it's built to refuse
/// answering outside retrieved context, which is the opposite instinct
/// from "sure, I'll draft you a document") and would be far less
/// predictable than a plain substring check, especially against a small
/// local model.
///
/// Conservative by design — a creation verb ("write"/"generate"/etc.) must
/// appear together with a document-ish noun ("document"/"report"/"file"/
/// etc.), OR the message names a specific format explicitly (".docx",
/// "markdown", "pdf"...). False negatives (a genuine document request
/// that doesn't match the wording) are more likely than false positives
/// with this design — the safer failure mode here: missing the export
/// button is a minor inconvenience, offering it on every ordinary
/// question would just be noise. Known gap: no real language
/// understanding — e.g. "read the report" would false-positive (verb list
/// doesn't include "read" so it actually wouldn't here, but the general
/// point stands: this is substring matching, not intent parsing).
/// </summary>
public static class DocumentGenerationIntentDetector
{
    private static readonly string[] CreationVerbs =
    [
        "write", "generate", "create", "draft", "produce", "compose", "make",
        "export", "put together", "prepare", "save this as", "turn this into"
    ];

    private static readonly string[] DocumentNouns =
    [
        "document", "doc", "report", "file", "memo", "letter", "write-up", "writeup"
    ];

    public static DocumentGenerationIntent? Detect(string? userMessage)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return null;
        }

        var lower = userMessage.ToLowerInvariant();

        var mentionsPdf = lower.Contains(".pdf") || lower.Contains(" pdf");
        var mentionsMarkdown = lower.Contains(".md") || lower.Contains("markdown");
        var mentionsPlainText = lower.Contains(".txt") || lower.Contains("plain text") || lower.Contains("text file");
        var mentionsWord = lower.Contains(".docx") || lower.Contains("word doc") || lower.Contains(" docx");

        var hasExplicitFormat = mentionsPdf || mentionsMarkdown || mentionsPlainText || mentionsWord;
        var hasCreationVerb = Array.Exists(CreationVerbs, lower.Contains);
        var hasDocumentNoun = Array.Exists(DocumentNouns, lower.Contains);

        if (!hasExplicitFormat && !(hasCreationVerb && hasDocumentNoun))
        {
            return null;
        }

        // Markdown/plain-text only trigger when explicitly named; anything
        // else (including an explicit "pdf" request — see
        // PdfRequestedButUnsupported below) falls back to Word, the
        // configured default.
        var format = mentionsMarkdown ? DocumentExportFormat.Markdown
            : mentionsPlainText ? DocumentExportFormat.PlainText
            : DocumentExportFormat.Word;

        return new DocumentGenerationIntent
        {
            Format = format,
            PdfRequestedButUnsupported = mentionsPdf && !mentionsMarkdown && !mentionsPlainText
        };
    }
}
