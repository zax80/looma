using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Looma.Application.Extraction;
using Xunit;

namespace Looma.Application.Tests;

/// <summary>
/// Builds a real .docx at test time via the Open XML SDK's own writer API
/// and round-trips it through <see cref="DocumentTextExtractor"/>.
/// </summary>
public sealed class DocumentTextExtractorDocxTests : IDisposable
{
    private readonly string _tempDocxPath = Path.Combine(Path.GetTempPath(), $"looma-test-{Guid.NewGuid():N}.docx");

    [Fact]
    public async Task ExtractAsync_Docx_RecoversParagraphText()
    {
        using (var document = WordprocessingDocument.Create(_tempDocxPath, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            mainPart.Document = new Document();
            var body = mainPart.Document.AppendChild(new Body());

            var firstParagraph = body.AppendChild(new Paragraph());
            firstParagraph.AppendChild(new Run(new Text("Looma uses Qdrant as its vector database.")));

            var secondParagraph = body.AppendChild(new Paragraph());
            secondParagraph.AppendChild(new Run(new Text("Only one vector store is used, on purpose.")));

            mainPart.Document.Save();
        }

        var text = await DocumentTextExtractor.ExtractAsync(_tempDocxPath);

        Assert.Contains("Looma", text);
        Assert.Contains("Qdrant", text);
        Assert.Contains("Only one vector store", text);
    }

    [Fact]
    public void IsSupported_Docx_ReturnsTrue()
    {
        Assert.True(DocumentTextExtractor.IsSupported(".docx"));
    }

    public void Dispose()
    {
        if (File.Exists(_tempDocxPath))
        {
            File.Delete(_tempDocxPath);
        }
    }
}
