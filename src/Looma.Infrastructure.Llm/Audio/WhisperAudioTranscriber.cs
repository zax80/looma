using System.Runtime.CompilerServices;
using Looma.Core.Abstractions;
using Looma.Core.Entities;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Whisper.net;

namespace Looma.Infrastructure.Llm.Audio;

/// <summary>
/// Local speech-to-text via Whisper.net (a .NET binding for whisper.cpp) —
/// no network call, matches the brief's stated choice ("Local via
/// Whisper.net, ONNX-based" — the "ONNX-based" part of that description is
/// actually inaccurate; whisper.cpp uses its own GGML format, not ONNX, but
/// Whisper.net is still the right library per the brief's intent). See
/// docs/model-setup.md for where <c>Models.SpeechToTextModel.ModelPath</c>
/// (a GGML file) comes from.
///
/// Whisper only accepts 16kHz mono PCM WAV — <see cref="NormalizeTo16KhzMonoWav"/>
/// converts whatever it's actually given (WAV or MP3, any sample rate,
/// mono or stereo) using the exact NAudio APIs Whisper.net's own examples
/// use (WdlResamplingSampleProvider, StereoToMonoSampleProvider,
/// Mp3FileReader) rather than reimplementing resampling by hand.
/// </summary>
public sealed class WhisperAudioTranscriber : IAudioTranscriber, IDisposable
{
    private readonly string _modelPath;
    private readonly Lazy<WhisperFactory> _factory;

    public WhisperAudioTranscriber(string modelPath)
    {
        _modelPath = modelPath;

        // Lazy, not eager in the constructor — same reasoning as
        // OnnxClipImageEmbeddingGenerator: this is a DI singleton
        // constructed at startup regardless of whether the run ever
        // touches audio, so loading the GGML model (and failing loudly if
        // it's missing) should happen on first real use, not block every
        // CLI invocation on a file that might not be needed.
        _factory = new Lazy<WhisperFactory>(CreateFactory);
    }

    public async IAsyncEnumerable<TranscriptSegment> TranscribeAsync(
        Stream audioStream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await audioStream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        var audioBytes = buffer.ToArray();

        using var normalizedWav = NormalizeTo16KhzMonoWav(audioBytes);

        using var processor = _factory.Value.CreateBuilder()
            .WithLanguage("auto")
            .Build();

        await foreach (var result in processor.ProcessAsync(normalizedWav).WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            var text = result.Text?.Trim() ?? string.Empty;
            if (text.Length == 0)
            {
                // Whisper occasionally emits an empty/silence segment —
                // not worth a zero-content chunk downstream.
                continue;
            }

            yield return new TranscriptSegment { Text = text, Start = result.Start, End = result.End };
        }
    }

    /// <summary>
    /// Detects the container (WAV vs MP3) from magic bytes, decodes it, and
    /// converts to 16kHz mono 16-bit PCM WAV — the one format Whisper
    /// actually accepts (anything else throws NotSupportedWaveException
    /// deep inside Whisper.net with a much less useful error message than
    /// getting this right up front).
    /// </summary>
    private static MemoryStream NormalizeTo16KhzMonoWav(byte[] audioBytes)
    {
        var format = AudioMediaTypeSniffer.Detect(audioBytes, fallbackExtension: ".wav");

        using var sourceStream = new MemoryStream(audioBytes);
        using WaveStream reader = format switch
        {
            AudioFormat.Wav => new WaveFileReader(sourceStream),
            AudioFormat.Mp3 => new Mp3FileReader(sourceStream),
            _ => throw new NotSupportedException($"Unsupported audio format: {format}.")
        };

        ISampleProvider sampleProvider = reader.ToSampleProvider();

        if (sampleProvider.WaveFormat.Channels == 2)
        {
            sampleProvider = new StereoToMonoSampleProvider(sampleProvider) { LeftVolume = 0.5f, RightVolume = 0.5f };
        }
        else if (sampleProvider.WaveFormat.Channels != 1)
        {
            // Rare (e.g. 5.1 surround) — fail clearly rather than guessing
            // a downmix that might not sound right.
            throw new NotSupportedException(
                $"Unsupported channel count: {sampleProvider.WaveFormat.Channels}. Only mono and stereo audio is supported.");
        }

        if (sampleProvider.WaveFormat.SampleRate != 16000)
        {
            sampleProvider = new WdlResamplingSampleProvider(sampleProvider, 16000);
        }

        var wavStream = new MemoryStream();
        WaveFileWriter.WriteWavFileToStream(wavStream, sampleProvider.ToWaveProvider16());
        wavStream.Seek(0, SeekOrigin.Begin);
        return wavStream;
    }

    private WhisperFactory CreateFactory()
    {
        if (!File.Exists(_modelPath))
        {
            throw new FileNotFoundException(
                $"Whisper GGML model not found at '{_modelPath}' (Models.SpeechToTextModel.ModelPath in config.json). " +
                "See docs/model-setup.md.",
                _modelPath);
        }

        return WhisperFactory.FromPath(_modelPath);
    }

    public void Dispose()
    {
        if (_factory.IsValueCreated)
        {
            _factory.Value.Dispose();
        }
    }
}
