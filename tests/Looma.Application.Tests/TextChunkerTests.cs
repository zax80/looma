using Looma.Application.Chunking;
using Xunit;

namespace Looma.Application.Tests;

/// <summary>
/// Pure logic, no external service required. Boundary cases here matter a
/// lot — this is the code standing between "real chunking" and quietly
/// reintroducing the truncation bug CLAUDE.md warns about.
/// </summary>
public sealed class TextChunkerTests
{
    [Fact]
    public void Chunk_EmptyText_ReturnsNoChunks()
    {
        Assert.Empty(TextChunker.Chunk(string.Empty, chunkSize: 10, overlap: 2));
    }

    [Fact]
    public void Chunk_TextShorterThanChunkSize_ReturnsExactlyOneChunkCoveringEverything()
    {
        var chunks = TextChunker.Chunk("short text", chunkSize: 400, overlap: 50);

        var chunk = Assert.Single(chunks);
        Assert.Equal("short text", chunk.Content);
        Assert.Equal(0, chunk.Index);
        Assert.Equal(1, chunk.StartLine);
        Assert.Equal(1, chunk.EndLine);
    }

    [Fact]
    public void Chunk_LongerText_CoversTheEntireInputWithNoGaps()
    {
        // No newlines at all — a single "line" longer than chunkSize, which
        // exercises the character-window fallback (see TextChunker's class
        // doc comment) rather than the normal whole-line packing path.
        var text = string.Concat(Enumerable.Repeat("abcdefghijklmnopqrstuvwxyz", 5)); // 130 chars

        var chunks = TextChunker.Chunk(text, chunkSize: 20, overlap: 5);

        // Reconstruct: with step = chunkSize - overlap, the last chunk's end
        // offset must reach the end of the text — nothing past the last
        // chunk boundary is silently dropped.
        var step = 20 - 5;
        var lastChunkStart = (chunks.Count - 1) * step;
        Assert.Equal(text.Length, lastChunkStart + chunks[^1].Content.Length);
        Assert.EndsWith(chunks[^1].Content, text);
    }

    [Fact]
    public void Chunk_ConsecutiveChunks_OverlapByTheConfiguredAmount()
    {
        // Same "one oversized line, no newlines" fallback path as above.
        var text = string.Concat(Enumerable.Repeat("abcdefghijklmnopqrstuvwxyz", 5));

        var chunks = TextChunker.Chunk(text, chunkSize: 20, overlap: 5);

        Assert.True(chunks.Count > 1);
        for (var i = 1; i < chunks.Count; i++)
        {
            var previousTail = chunks[i - 1].Content[^5..];
            var currentHead = chunks[i].Content[..5];
            Assert.Equal(previousTail, currentHead);
        }
    }

    [Fact]
    public void Chunk_NeverSplitsALineAcrossAChunkBoundary()
    {
        // Regression test for a real bug: pure character-window chunking
        // split "IVAN SPAHIYSKI" (a DOCX-extracted form field, one line) at
        // a chunk boundary into "IVA" + "N SPAHIYSKI" — the retrieved chunk
        // no longer contained the actual name. Short label/value lines like
        // this must always land in a chunk whole.
        var text = "Contact Person (required)\nIVAN SPAHIYSKI\nPosition (required)\nOwner\n" +
                    "Email (required)\nispahiyski@gmail.com\n";

        var chunks = TextChunker.Chunk(text, chunkSize: 40, overlap: 10);

        foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.Contains(chunks, chunk => chunk.Content.Contains(line));
        }
    }

    [Fact]
    public void Chunk_LineLongerThanChunkSize_StillGetsCharacterSlicedRatherThanSkipped()
    {
        // The one deliberate exception to "never split a line": a single
        // line that alone exceeds chunkSize still has to be split
        // somewhere, or chunkSize would be meaningless for unbroken text.
        var longLine = string.Concat(Enumerable.Repeat("word ", 20)); // 100 chars, one line

        var chunks = TextChunker.Chunk(longLine, chunkSize: 30, overlap: 5);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.Content.Length <= 30));
    }

    [Fact]
    public void Chunk_TracksLineRanges_AcrossMultilineText()
    {
        var text = "line1\nline2\nline3\nline4\nline5\n";

        var chunks = TextChunker.Chunk(text, chunkSize: 12, overlap: 4);

        Assert.Equal(1, chunks[0].StartLine);
        Assert.Equal(2, chunks[0].EndLine);
        // Every chunk's line range should be non-decreasing across the run.
        for (var i = 1; i < chunks.Count; i++)
        {
            Assert.True(chunks[i].StartLine >= chunks[i - 1].StartLine);
            Assert.True(chunks[i].EndLine >= chunks[i].StartLine);
        }
        Assert.Equal(5, chunks[^1].EndLine);
    }

    [Theory]
    [InlineData(0, 5)]
    [InlineData(-1, 5)]
    public void Chunk_NonPositiveChunkSize_Throws(int chunkSize, int overlap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextChunker.Chunk("text", chunkSize, overlap));
    }

    [Fact]
    public void Chunk_NegativeOverlap_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TextChunker.Chunk("text", chunkSize: 10, overlap: -1));
    }

    [Fact]
    public void Chunk_OverlapNotSmallerThanChunkSize_Throws()
    {
        Assert.Throws<ArgumentException>(() => TextChunker.Chunk("text", chunkSize: 10, overlap: 10));
    }
}
