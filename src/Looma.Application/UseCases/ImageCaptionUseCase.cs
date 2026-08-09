using Looma.Core.Abstractions;
using Looma.Core.Entities;

namespace Looma.Application.UseCases;

public sealed class ImageCaptionUseCase : IImageCaptionUseCase
{
    private readonly IImageCaptioner _imageCaptioner;

    public ImageCaptionUseCase(IImageCaptioner imageCaptioner)
    {
        _imageCaptioner = imageCaptioner;
    }

    public Task<ImageCaptionResult> CaptionAsync(Stream imageStream, CancellationToken cancellationToken = default) =>
        _imageCaptioner.CaptionAsync(imageStream, cancellationToken);
}
