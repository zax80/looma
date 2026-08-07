using Looma.Core.Entities;

namespace Looma.Application.Chunking;

/// <summary>
/// The audio counterpart to <see cref="TextChunker"/>: whole
/// <see cref="TranscriptSegment"/>s (Whisper's own sentence/phrase-level
/// segmentation) are packed into each chunk up to <c>chunkSize</c>
/// characters and never split mid-segment — same "never split a unit"
/// principle that fixed the real line-splitting bug <see cref="TextChunker"/>'s
/// doc comment describes, applied to segments instead of lines. Consecutive
/// chunks overlap by whole trailing/leading segments totalling roughly (not
/// exactly) <c>overlap</c> characters.
///
/// Unlike <see cref="TextChunker"/>, there's no oversized-single-unit
/// fallback here: a single Whisper segment is normally one sentence or
/// phrase, never a multi-hundred-character block, so an oversized segment
/// (however rare) is still included whole rather than sliced — slicing
/// transcribed speech mid-sentence would corrupt it the same way slicing
/// "IVAN SPAHIYSKI" did, with no natural break to fall back to within a
/// single segment the way a line has none either, but at much smaller and
/// more forgivable scale.
/// </summary>
public static class TranscriptChunker
{
    public static IReadOnlyList<TranscriptChunk> Chunk(IReadOnlyList<TranscriptSegment> segments, int chunkSize, int overlap)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), chunkSize, "Chunk size must be positive.");
        }

        if (overlap < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(overlap), overlap, "Overlap cannot be negative.");
        }

        if (overlap >= chunkSize)
        {
            throw new ArgumentException("Overlap must be smaller than chunk size, or chunking would never advance.", nameof(overlap));
        }

        if (segments.Count == 0)
        {
            return [];
        }

        var chunks = new List<TranscriptChunk>();
        var chunkIndex = 0;
        var i = 0;

        while (i < segments.Count)
        {
            // Pack whole segments until adding the next one would exceed
            // chunkSize. The first segment always goes in, so this always
            // makes forward progress even if it alone exceeds chunkSize.
            var j = i;
            var length = 0;

            while (j < segments.Count)
            {
                var segmentLength = segments[j].Text.Length;
                if (length > 0 && length + segmentLength > chunkSize)
                {
                    break;
                }

                length += segmentLength;
                j++;

                if (length >= chunkSize)
                {
                    break;
                }
            }

            var content = string.Join(' ', segments.Skip(i).Take(j - i).Select(s => s.Text));
            chunks.Add(new TranscriptChunk(chunkIndex++, content, segments[i].Start, segments[j - 1].End));

            if (j >= segments.Count)
            {
                break;
            }

            // Overlap: walk back from j including whole segments until their
            // combined text length reaches `overlap`, so the next chunk
            // starts there instead of exactly at j. Never splits a segment
            // to hit the overlap amount exactly.
            var overlapChars = 0;
            var k = j;
            while (k > i && overlapChars < overlap)
            {
                k--;
                overlapChars += segments[k].Text.Length;
            }

            // Guarantee forward progress: if a single segment's length alone
            // is >= overlap, walking back for overlap lands right back on
            // the chunk boundary we just built (k == i), which would loop
            // forever re-emitting the same chunk. Skip overlap for that one
            // boundary rather than getting stuck.
            i = k > i ? k : j;
        }

        return chunks;
    }
}
