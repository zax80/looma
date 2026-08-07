using Looma.Infrastructure.Llm.Vision;
using Xunit;

namespace Looma.Infrastructure.Llm.Tests;

/// <summary>
/// Tests <see cref="OllamaImageCaptioner.ParseResponse"/> only — the actual
/// model call needs a real vision model, so that half can only be verified
/// by running <c>looma index</c> against real image files locally.
/// </summary>
public sealed class OllamaImageCaptionerParseResponseTests
{
    [Fact]
    public void ParseResponse_WellFormedTwoLineResponse_SplitsCaptionAndText()
    {
        var result = OllamaImageCaptioner.ParseResponse(
            "Caption: A red coffee mug on a wooden table.\nText: none");

        Assert.Equal("A red coffee mug on a wooden table.", result.Caption);
        Assert.Null(result.OcrText);
    }

    [Fact]
    public void ParseResponse_WithRealOcrText_PopulatesOcrText()
    {
        var result = OllamaImageCaptioner.ParseResponse(
            "Caption: A certificate with a gold seal.\nText: Certificate of Achievement - Ivan Spahiyski");

        Assert.Equal("A certificate with a gold seal.", result.Caption);
        Assert.Equal("Certificate of Achievement - Ivan Spahiyski", result.OcrText);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("None")]
    [InlineData("NONE")]
    [InlineData("")]
    [InlineData("   ")]
    public void ParseResponse_NoLegibleText_OcrTextIsNull(string textLine)
    {
        var result = OllamaImageCaptioner.ParseResponse($"Caption: A blank wall.\nText: {textLine}");

        Assert.Null(result.OcrText);
    }

    [Fact]
    public void ParseResponse_MultilineCaptionAndText_JoinsLinesWithinEachSection()
    {
        var result = OllamaImageCaptioner.ParseResponse(
            "Caption: A scanned document.\nIt has two paragraphs of text.\nText: Line one\nLine two");

        Assert.Equal("A scanned document.\nIt has two paragraphs of text.", result.Caption);
        Assert.Equal("Line one\nLine two", result.OcrText);
    }

    [Fact]
    public void ParseResponse_NoLabelsAtAll_WholeResponseBecomesCaption()
    {
        var result = OllamaImageCaptioner.ParseResponse("Just a photo of a mountain at sunset.");

        Assert.Equal("Just a photo of a mountain at sunset.", result.Caption);
        Assert.Null(result.OcrText);
    }

    [Fact]
    public void ParseResponse_EmptyResponse_ReturnsEmptyCaption()
    {
        var result = OllamaImageCaptioner.ParseResponse(string.Empty);

        Assert.Equal(string.Empty, result.Caption);
        Assert.Null(result.OcrText);
    }

    [Fact]
    public void ParseResponse_CaptionMentioningTheWordText_DoesNotConfuseTheSplitter()
    {
        // Regression guard for the earlier index-slicing approach: a caption
        // that legitimately contains the substring "text" (not as a line
        // label) must not get truncated at that point.
        var result = OllamaImageCaptioner.ParseResponse(
            "Caption: A page of text with a large heading.\nText: WELCOME");

        Assert.Equal("A page of text with a large heading.", result.Caption);
        Assert.Equal("WELCOME", result.OcrText);
    }
}
