namespace Looma.Application.Chunking;

/// <summary>One chunk produced by <see cref="TextChunker"/>, with 1-based inclusive line range.</summary>
public sealed record TextChunk(int Index, string Content, int StartLine, int EndLine);
