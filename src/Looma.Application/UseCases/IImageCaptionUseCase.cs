using Looma.Core.Entities;

namespace Looma.Application.UseCases;

/// <summary>
/// Ad-hoc vision captioning for a single image (e.g. a chat attachment) —
/// NOT indexing: no CLIP embedding, no storage, just a caption back.
/// Contrast with the indexing pipeline's own image handling, which stores
/// the result in the <c>images</c> vector collection.
/// </summary>
public interface IImageCaptionUseCase
{
    Task<ImageCaptionResult> CaptionAsync(Stream imageStream, CancellationToken cancellationToken = default);
}
