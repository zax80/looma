namespace Looma.Application.Chunking;

/// <summary>One chunk produced by <see cref="TranscriptChunker"/>, with the timestamp range it spans.</summary>
public sealed record TranscriptChunk(int Index, string Content, TimeSpan StartTime, TimeSpan EndTime);
