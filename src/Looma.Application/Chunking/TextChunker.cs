namespace Looma.Application.Chunking;

/// <summary>
/// Real chunking-with-overlap — never truncation, and (as of this version)
/// never splitting a line in the middle either, unless a single line alone
/// is longer than <c>chunkSize</c> (see below). A prior version of this
/// project truncated documents to 2000 characters as a "temporary" stopgap
/// that never got fixed (see CLAUDE.md Lessons); this type exists
/// specifically so nothing in the ingestion path can take that shortcut
/// again.
///
/// Line-atomic packing, not a pure character window: a real run showed why
/// pure character-window slicing is actively harmful, not just imprecise —
/// it split "IVAN SPAHIYSKI" across a chunk boundary into "IVA" + "N
/// SPAHIYSKI", and the retrieved chunk containing the tail no longer
/// contained the person's actual name. Whole lines are now packed into each
/// chunk up to <c>chunkSize</c> characters and are never split mid-line;
/// consecutive chunks overlap by whole trailing/leading lines totalling
/// roughly (not exactly) <c>overlap</c> characters.
///
/// The one exception: a single line longer than <c>chunkSize</c> on its own
/// (e.g. one very long unbroken paragraph with no line breaks at all) falls
/// back to plain character-window slicing for just that line — there's no
/// line boundary left to respect within it, and chunkSize still needs to be
/// honored so a pathologically long line doesn't produce one unbounded chunk.
/// </summary>
public static class TextChunker
{
    public static IReadOnlyList<TextChunk> Chunk(string text, int chunkSize, int overlap)
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

        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var lineStartOffsets = ComputeLineStartOffsets(text);
        var lines = SplitIntoLines(text);
        var step = chunkSize - overlap;

        var chunks = new List<TextChunk>();
        var chunkIndex = 0;
        var i = 0;

        while (i < lines.Count)
        {
            var (lineStart, lineEnd) = lines[i];
            if (lineEnd - lineStart > chunkSize)
            {
                // This one line doesn't fit on its own — fall back to
                // character-window slicing for just this line, the same
                // way every chunk used to be built.
                var position = lineStart;
                while (position < lineEnd)
                {
                    var length = Math.Min(chunkSize, lineEnd - position);
                    chunks.Add(new TextChunk(
                        Index: chunkIndex++,
                        Content: text.Substring(position, length),
                        StartLine: LineNumberAt(lineStartOffsets, position),
                        EndLine: LineNumberAt(lineStartOffsets, position + length - 1)));

                    var reachedLineEnd = position + length >= lineEnd;
                    if (reachedLineEnd)
                    {
                        break;
                    }

                    position += step;
                }

                i++;
                continue;
            }

            // Pack whole lines until adding the next one would exceed
            // chunkSize. The first line always goes in (even alone it's
            // already known to fit, from the check above), so this always
            // makes forward progress.
            var start = lineStart;
            var j = i;
            var length2 = 0;

            while (j < lines.Count)
            {
                var currentLineLength = lines[j].End - lines[j].Start;
                if (currentLineLength > chunkSize)
                {
                    // Stop before an oversized line — it's handled by the
                    // fallback above on its own, next time through the outer loop.
                    break;
                }

                if (length2 > 0 && length2 + currentLineLength > chunkSize)
                {
                    break;
                }

                length2 += currentLineLength;
                j++;

                if (length2 >= chunkSize)
                {
                    break;
                }
            }

            var end = lines[j - 1].End;

            chunks.Add(new TextChunk(
                Index: chunkIndex++,
                Content: text[start..end],
                StartLine: LineNumberAt(lineStartOffsets, start),
                EndLine: LineNumberAt(lineStartOffsets, end - 1)));

            if (j >= lines.Count)
            {
                break;
            }

            // Overlap: walk back from j including whole lines until their
            // combined length reaches `overlap`, so the next chunk starts
            // there instead of exactly at j. Never splits a line to hit the
            // overlap amount exactly.
            var overlapChars = 0;
            var k = j;
            while (k > i && overlapChars < overlap)
            {
                k--;
                overlapChars += lines[k].End - lines[k].Start;
            }

            // Guarantee forward progress: if a single line's length alone
            // is >= overlap, walking back for overlap lands right back on
            // the chunk boundary we just built (k == i), which would loop
            // forever re-emitting the same chunk. Skip overlap for that one
            // boundary rather than getting stuck.
            i = k > i ? k : j;
        }

        return chunks;
    }

    /// <summary>
    /// Splits <paramref name="text"/> into line spans, each running from a
    /// line's first character through and including its trailing '\n'
    /// (the final line omits the trailing '\n' if the text doesn't end with
    /// one). Concatenating every span in order reconstructs the original
    /// text exactly — this is what guarantees chunking never drops content,
    /// independent of how chunks are packed from these lines.
    /// </summary>
    private static List<(int Start, int End)> SplitIntoLines(string text)
    {
        var lines = new List<(int Start, int End)>();
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lines.Add((start, i + 1));
                start = i + 1;
            }
        }

        if (start < text.Length)
        {
            lines.Add((start, text.Length));
        }

        return lines;
    }

    /// <summary>Character offset (0-based) at which each line (0-based) starts.</summary>
    private static List<int> ComputeLineStartOffsets(string text)
    {
        var offsets = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                offsets.Add(i + 1);
            }
        }

        return offsets;
    }

    /// <summary>1-based line number containing 0-based character offset <paramref name="charOffset"/>.</summary>
    private static int LineNumberAt(List<int> lineStartOffsets, int charOffset)
    {
        // Binary search for the last line-start offset <= charOffset.
        var lo = 0;
        var hi = lineStartOffsets.Count - 1;
        var result = 0;

        while (lo <= hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (lineStartOffsets[mid] <= charOffset)
            {
                result = mid;
                lo = mid + 1;
            }
            else
            {
                hi = mid - 1;
            }
        }

        return result + 1; // 1-based
    }
}
