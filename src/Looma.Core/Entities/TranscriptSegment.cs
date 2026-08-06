namespace Looma.Core.Entities;

/// <summary>One timestamped segment of a streamed audio transcription.</summary>
public sealed record TranscriptSegment
{
    public required string Text { get; init; }
    public required TimeSpan Start { get; init; }
    public required TimeSpan End { get; init; }
}
