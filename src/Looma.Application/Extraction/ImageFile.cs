namespace Looma.Application.Extraction;

/// <summary>
/// Extension check for the image ingestion path, mirroring
/// <see cref="DocumentTextExtractor"/>'s shape. Kept as a separate small type
/// rather than folded into <see cref="DocumentTextExtractor"/> because images
/// don't go through a "text extractor" at all — they go through a model call
/// (captioning + CLIP embedding) in <c>IndexingUseCase</c>, not a parsing
/// library.
/// </summary>
public static class ImageFile
{
    public static readonly IReadOnlyList<string> SupportedExtensions = [".png", ".jpg", ".jpeg"];

    public static bool IsSupported(string extension) =>
        SupportedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
}
