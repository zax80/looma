using Looma.Application.Extraction;
using Xunit;

namespace Looma.Application.Tests;

public sealed class DocumentTextExtractorTests : IDisposable
{
    private readonly string _tempTxtPath = Path.Combine(Path.GetTempPath(), $"looma-test-{Guid.NewGuid():N}.txt");

    [Fact]
    public async Task ExtractAsync_PlainText_ReturnsFileContentVerbatim()
    {
        const string content = "Looma uses Qdrant as its vector database.";
        await File.WriteAllTextAsync(_tempTxtPath, content);

        var text = await DocumentTextExtractor.ExtractAsync(_tempTxtPath);

        Assert.Equal(content, text);
    }

    [Fact]
    public async Task ExtractAsync_UnsupportedExtension_Throws()
    {
        var unsupportedPath = Path.Combine(Path.GetTempPath(), $"looma-test-{Guid.NewGuid():N}.png");

        await Assert.ThrowsAsync<NotSupportedException>(() => DocumentTextExtractor.ExtractAsync(unsupportedPath));
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".md")]
    [InlineData(".csv")]
    [InlineData(".pdf")]
    [InlineData(".docx")]
    [InlineData(".xlsx")]
    public void IsSupported_KnownExtensions_ReturnsTrue(string extension)
    {
        Assert.True(DocumentTextExtractor.IsSupported(extension));
    }

    [Theory]
    [InlineData(".png")]
    [InlineData(".wav")]
    [InlineData(".jpg")]
    public void IsSupported_MediaExtensions_ReturnsFalse(string extension)
    {
        Assert.False(DocumentTextExtractor.IsSupported(extension));
    }

    public void Dispose()
    {
        if (File.Exists(_tempTxtPath))
        {
            File.Delete(_tempTxtPath);
        }
    }
}
