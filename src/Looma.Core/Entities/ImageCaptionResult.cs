namespace Looma.Core.Entities;

/// <summary>Result of running a vision-language model over an image: caption + OCR, in one pass.</summary>
public sealed record ImageCaptionResult
{
    public required string Caption { get; init; }
    public string? OcrText { get; init; }
}
