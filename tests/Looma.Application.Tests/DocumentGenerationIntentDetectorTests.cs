using Looma.Application.DocumentGeneration;
using Looma.Core.Entities;
using Xunit;

namespace Looma.Application.Tests;

public class DocumentGenerationIntentDetectorTests
{
    [Theory]
    [InlineData("Write this up as a report")]
    [InlineData("Can you generate a summary document for me?")]
    [InlineData("Please create a memo about this")]
    [InlineData("draft a letter based on what we discussed")]
    [InlineData("export this as a file")]
    [InlineData("turn this into a write-up")]
    public void Detect_CreationVerbPlusDocumentNoun_ReturnsIntent(string message)
    {
        var intent = DocumentGenerationIntentDetector.Detect(message);

        Assert.NotNull(intent);
    }

    [Theory]
    [InlineData("What's in the quarterly report?")]
    [InlineData("Summarize the document I indexed")]
    [InlineData("What does the memo say?")]
    [InlineData("Tell me about the first tree")]
    [InlineData("")]
    [InlineData(null)]
    public void Detect_NoCreationIntent_ReturnsNull(string? message)
    {
        var intent = DocumentGenerationIntentDetector.Detect(message);

        Assert.Null(intent);
    }

    [Fact]
    public void Detect_NoExplicitFormat_DefaultsToWord()
    {
        var intent = DocumentGenerationIntentDetector.Detect("Write this up as a report");

        Assert.NotNull(intent);
        Assert.Equal(DocumentExportFormat.Word, intent!.Format);
    }

    [Theory]
    [InlineData("Generate a markdown report", DocumentExportFormat.Markdown)]
    [InlineData("Write this as a .md file", DocumentExportFormat.Markdown)]
    [InlineData("Create a plain text file with this", DocumentExportFormat.PlainText)]
    [InlineData("Export this as a .txt document", DocumentExportFormat.PlainText)]
    [InlineData("Generate a .docx report", DocumentExportFormat.Word)]
    [InlineData("Generate this as a pdf", DocumentExportFormat.Pdf)]
    [InlineData("Write this up as a .pdf", DocumentExportFormat.Pdf)]
    public void Detect_ExplicitFormat_ReturnsThatFormat(string message, DocumentExportFormat expected)
    {
        var intent = DocumentGenerationIntentDetector.Detect(message);

        Assert.NotNull(intent);
        Assert.Equal(expected, intent!.Format);
    }

    [Fact]
    public void Detect_ExplicitExtensionAlone_DoesNotNeedCreationVerb()
    {
        // ".docx" mentioned on its own, no "write"/"generate"/etc. — still
        // a strong enough signal per the detector's own doc comment.
        var intent = DocumentGenerationIntentDetector.Detect("I need this in .docx format please");

        Assert.NotNull(intent);
        Assert.Equal(DocumentExportFormat.Word, intent!.Format);
    }
}
