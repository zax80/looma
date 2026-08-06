using Looma.Core.Entities;

namespace Looma.Core.Abstractions;

/// <summary>Local vision-language captioning + OCR (e.g. Qwen2.5-VL via Ollama).</summary>
public interface IImageCaptioner
{
    Task<ImageCaptionResult> CaptionAsync(
        Stream imageStream,
        CancellationToken cancellationToken = default);
}
