using System.Text;
using Looma.Core.Abstractions;

namespace Looma.Application.UseCases;

public sealed class TranscriptionUseCase : ITranscriptionUseCase
{
    private readonly IAudioTranscriber _audioTranscriber;

    public TranscriptionUseCase(IAudioTranscriber audioTranscriber)
    {
        _audioTranscriber = audioTranscriber;
    }

    public async Task<string> TranscribeAsync(Stream audioStream, CancellationToken cancellationToken = default)
    {
        var text = new StringBuilder();
        await foreach (var segment in _audioTranscriber.TranscribeAsync(audioStream, cancellationToken).ConfigureAwait(false))
        {
            if (text.Length > 0)
            {
                text.Append(' ');
            }

            text.Append(segment.Text.Trim());
        }

        return text.ToString();
    }
}
