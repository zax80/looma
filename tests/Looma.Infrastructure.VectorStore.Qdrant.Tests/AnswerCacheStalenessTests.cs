using Looma.Core.Entities;
using Looma.Infrastructure.VectorStore.Qdrant.Internal;
using Xunit;

namespace Looma.Infrastructure.VectorStore.Qdrant.Tests;

public sealed class AnswerCacheStalenessTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 18, 12, 0, 0, TimeSpan.Zero);

    private static DocumentChunk MakeCitation(MediaType mediaType) => new()
    {
        Id = Guid.NewGuid().ToString(),
        SourceId = "source",
        Content = "chunk text",
        Metadata = new ChunkMetadata
        {
            SourcePath = "source",
            MediaType = mediaType,
            ChunkIndex = 0,
            IndexedAtUtc = Now
        }
    };

    [Fact]
    public void IsStale_NoWebCitation_NeverStale()
    {
        var citations = new List<DocumentChunk> { MakeCitation(MediaType.Text) };

        var result = AnswerCacheStaleness.IsStale(citations, Now.AddYears(-1), webCitationMaxAgeHours: 24, now: Now);

        Assert.False(result);
    }

    [Fact]
    public void IsStale_WebCitationWithinMaxAge_NotStale()
    {
        var citations = new List<DocumentChunk> { MakeCitation(MediaType.Web) };

        var result = AnswerCacheStaleness.IsStale(citations, Now.AddHours(-1), webCitationMaxAgeHours: 24, now: Now);

        Assert.False(result);
    }

    [Fact]
    public void IsStale_WebCitationPastMaxAge_IsStale()
    {
        var citations = new List<DocumentChunk> { MakeCitation(MediaType.Web) };

        var result = AnswerCacheStaleness.IsStale(citations, Now.AddHours(-25), webCitationMaxAgeHours: 24, now: Now);

        Assert.True(result);
    }

    [Fact]
    public void IsStale_MaxAgeHoursNull_DisablesCheckEvenWithOldWebCitation()
    {
        var citations = new List<DocumentChunk> { MakeCitation(MediaType.Web) };

        var result = AnswerCacheStaleness.IsStale(citations, Now.AddYears(-1), webCitationMaxAgeHours: null, now: Now);

        Assert.False(result);
    }

    [Fact]
    public void IsStale_MixOfWebAndNonWebCitations_EvaluatesAsWebSourced()
    {
        var citations = new List<DocumentChunk> { MakeCitation(MediaType.Text), MakeCitation(MediaType.Web) };

        var result = AnswerCacheStaleness.IsStale(citations, Now.AddHours(-25), webCitationMaxAgeHours: 24, now: Now);

        Assert.True(result);
    }
}
