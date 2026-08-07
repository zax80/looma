using Looma.Application.Chunking;
using Looma.Core.Entities;
using Xunit;

namespace Looma.Application.Tests;

/// <summary>
/// Mirrors TextChunkerTests' structure — same boundary cases, applied to
/// segments instead of lines.
/// </summary>
public sealed class TranscriptChunkerTests
{
    private static TranscriptSegment Seg(string text, double startSeconds, double endSeconds) => new()
    {
        Text = text,
        Start = TimeSpan.FromSeconds(startSeconds),
        End = TimeSpan.FromSeconds(endSeconds)
    };

    [Fact]
    public void Chunk_EmptySegments_ReturnsNoChunks()
    {
        Assert.Empty(TranscriptChunker.Chunk([], chunkSize: 100, overlap: 10));
    }

    [Fact]
    public void Chunk_FewShortSegments_ReturnsOneChunkCoveringAll()
    {
        var segments = new List<TranscriptSegment>
        {
            Seg("Hello there.", 0, 1),
            Seg("How are you?", 1, 2.5)
        };

        var chunks = TranscriptChunker.Chunk(segments, chunkSize: 400, overlap: 50);

        var chunk = Assert.Single(chunks);
        Assert.Equal("Hello there. How are you?", chunk.Content);
        Assert.Equal(TimeSpan.Zero, chunk.StartTime);
        Assert.Equal(TimeSpan.FromSeconds(2.5), chunk.EndTime);
    }

    [Fact]
    public void Chunk_NeverSplitsASegmentAcrossAChunkBoundary()
    {
        var segments = new List<TranscriptSegment>
        {
            Seg("This is segment one.", 0, 2),
            Seg("This is segment two.", 2, 4),
            Seg("This is segment three.", 4, 6),
            Seg("This is segment four.", 6, 8)
        };

        // chunkSize small enough that not all four segments fit in one chunk.
        var chunks = TranscriptChunker.Chunk(segments, chunkSize: 45, overlap: 10);

        Assert.True(chunks.Count > 1);
        foreach (var segment in segments)
        {
            Assert.Contains(chunks, chunk => chunk.Content.Contains(segment.Text));
        }
    }

    [Fact]
    public void Chunk_ConsecutiveChunks_TrackNonDecreasingTimeRanges()
    {
        var segments = Enumerable.Range(0, 10)
            .Select(i => Seg($"Segment number {i} here.", i * 2, (i * 2) + 2))
            .ToList();

        var chunks = TranscriptChunker.Chunk(segments, chunkSize: 60, overlap: 15);

        Assert.True(chunks.Count > 1);
        for (var i = 1; i < chunks.Count; i++)
        {
            Assert.True(chunks[i].StartTime >= chunks[i - 1].StartTime);
            Assert.True(chunks[i].EndTime >= chunks[i].StartTime);
        }

        Assert.Equal(segments[0].Start, chunks[0].StartTime);
        Assert.Equal(segments[^1].End, chunks[^1].EndTime);
    }

    [Fact]
    public void Chunk_SingleSegmentLongerThanChunkSize_StillIncludedWhole()
    {
        // The one deliberate difference from TextChunker: no character-slice
        // fallback for an oversized unit — a single Whisper segment is
        // included whole rather than corrupted mid-sentence.
        var longSegment = Seg(string.Concat(Enumerable.Repeat("word ", 30)), 0, 10); // 150 chars, one segment

        var chunks = TranscriptChunker.Chunk([longSegment], chunkSize: 50, overlap: 5);

        var chunk = Assert.Single(chunks);
        Assert.Equal(longSegment.Text, chunk.Content);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(-1, 5)]
    public void Chunk_NonPositiveChunkSize_Throws(int chunkSize, int overlap)
    {
        var segments = new List<TranscriptSegment> { Seg("text", 0, 1) };
        Assert.Throws<ArgumentOutOfRangeException>(() => TranscriptChunker.Chunk(segments, chunkSize, overlap));
    }

    [Fact]
    public void Chunk_NegativeOverlap_Throws()
    {
        var segments = new List<TranscriptSegment> { Seg("text", 0, 1) };
        Assert.Throws<ArgumentOutOfRangeException>(() => TranscriptChunker.Chunk(segments, chunkSize: 10, overlap: -1));
    }

    [Fact]
    public void Chunk_OverlapNotSmallerThanChunkSize_Throws()
    {
        var segments = new List<TranscriptSegment> { Seg("text", 0, 1) };
        Assert.Throws<ArgumentException>(() => TranscriptChunker.Chunk(segments, chunkSize: 10, overlap: 10));
    }
}
