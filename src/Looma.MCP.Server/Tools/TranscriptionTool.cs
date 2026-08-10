using System.ComponentModel;
using Looma.Application.UseCases;
using ModelContextProtocol.Server;

namespace Looma.MCP.Server.Tools;

/// <summary>
/// Wraps <see cref="ITranscriptionUseCase"/> — ad-hoc speech-to-text for
/// one chat voice-input recording, not indexing. A single call, no
/// streaming needed (same shape as <see cref="CountTool"/>): the whole
/// clip is short (a hold-to-record chat message, not a long file) and
/// Whisper only returns the final concatenated text anyway — see
/// <see cref="TranscriptionUseCase"/>'s doc comment.
///
/// The audio travels as a base64 string (<c>audioBase64</c>) rather than
/// an MCP binary resource — kept consistent with <c>looma_chat</c>'s
/// "plain string tool parameters, no separate binary transport" approach,
/// reasonable here since a single voice clip is small.
/// </summary>
[McpServerToolType]
public static class TranscriptionTool
{
    [McpServerTool(Name = "looma_transcribe", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Transcribes a single audio clip (base64-encoded WAV or MP3) to text. Ad-hoc — not indexed, just returns the transcript.")]
    public static async Task<string> Transcribe(
        ITranscriptionUseCase transcriptionUseCase,
        [Description("Base64-encoded audio bytes (WAV or MP3).")] string audioBase64,
        CancellationToken cancellationToken = default)
    {
        byte[] audioBytes;
        try
        {
            audioBytes = Convert.FromBase64String(audioBase64);
        }
        catch (FormatException ex)
        {
            throw new ModelContextProtocol.McpException($"audioBase64 isn't valid base64: {ex.Message}");
        }

        await using var stream = new MemoryStream(audioBytes);
        return await transcriptionUseCase.TranscribeAsync(stream, cancellationToken).ConfigureAwait(false);
    }
}
