using Microsoft.Extensions.Configuration;

namespace Looma.Infrastructure.Llm;

/// <summary>
/// The "direct ONNX/GGML fetch" half of the brief's model provisioning
/// story (docs/looma-project-brief.md section 6: "auto-pulled on first run
/// ... through Ollama for chat/embedding/vision, direct ONNX fetch for
/// Whisper/CLIP"). Mirrors <see cref="OllamaStartup"/>'s
/// "ensure-ready, fetch-if-missing" shape for models Ollama doesn't serve —
/// there's no daemon/process lifecycle to manage here, just a file that
/// either already exists or needs downloading once. Handles both CLIP
/// (ONNX) and Whisper (GGML) model files identically — the download logic
/// doesn't care about the file format, just that it's a single file at a
/// URL. (Originally named OnnxModelProvisioner when it only handled CLIP;
/// renamed once Whisper needed the exact same "ensure this local model file
/// exists" logic — the ONNX-specific name would have been actively
/// misleading for a GGML file.)
///
/// Unlike <see cref="OllamaStartup.EnsureOllamaReadyAsync"/>, a failure here
/// is deliberately NOT fatal to every command. BaseModel/EmbeddingModel are
/// genuinely required by every RAG operation, so failing loudly and
/// aborting is correct for those. CLIP and Whisper are each only needed for
/// their own media type's ingestion — a text-only `looma answer` or `looma
/// count` run shouldn't be blocked because one of these downloads failed on
/// a flaky connection. Callers should log the failure and continue;
/// <c>OnnxClipImageEmbeddingGenerator</c> / <c>WhisperAudioTranscriber</c>
/// still fail loudly and specifically if a run that actually needs the file
/// finds it missing.
/// </summary>
public static class LocalModelFileProvisioner
{
    public static Task EnsureImageEmbeddingModelReadyAsync(
        IConfiguration configuration,
        Action<string>? onStatus = null,
        CancellationToken cancellationToken = default)
    {
        var (llmOptions, _) = ServiceCollectionExtensions.BindOptions(configuration);
        return EnsureFileAsync(
            llmOptions.ImageEmbeddingModel.ModelPath,
            llmOptions.ImageEmbeddingModel.DownloadUrl,
            onStatus,
            cancellationToken);
    }

    public static Task EnsureSpeechToTextModelReadyAsync(
        IConfiguration configuration,
        Action<string>? onStatus = null,
        CancellationToken cancellationToken = default)
    {
        var (llmOptions, _) = ServiceCollectionExtensions.BindOptions(configuration);
        return EnsureFileAsync(
            llmOptions.SpeechToTextModel.ModelPath,
            llmOptions.SpeechToTextModel.DownloadUrl,
            onStatus,
            cancellationToken);
    }

    /// <summary>
    /// Public (not internal) specifically so the deterministic branches
    /// (already-present, missing-with-no-URL) are directly unit-testable
    /// without needing a real network call — this codebase doesn't use
    /// InternalsVisibleTo anywhere, so "testable" here means "public".
    /// </summary>
    public static async Task EnsureFileAsync(
        string? modelPath,
        string? downloadUrl,
        Action<string>? onStatus,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(modelPath))
        {
            return;
        }

        var fullPath = Path.GetFullPath(modelPath);
        if (File.Exists(fullPath))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(downloadUrl))
        {
            throw new FileNotFoundException(
                $"'{fullPath}' doesn't exist and no download URL is configured " +
                "to fetch it automatically. See docs/model-setup.md.",
                fullPath);
        }

        onStatus?.Invoke($"'{fullPath}' not found — downloading from {downloadUrl}...");

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        // A plain, unrestricted HttpClient — deliberately NOT run through the
        // same AllowedInferenceHosts/BlockNonLocalEndpoints locality check
        // Infrastructure.Llm's chat/embedding/vision clients use. That check
        // exists to guarantee runtime inference traffic — which can carry
        // document content — never leaves the system undetected. Fetching a
        // public model-weights file once, at provisioning time, is a
        // different category of network access: the same one `ollama pull`
        // already performs for the chat/embedding/vision models, just over
        // plain HTTP instead of Ollama's registry protocol.
        using var httpClient = new HttpClient();
        var tempPath = fullPath + ".download";
        try
        {
            using (var response = await httpClient
                .GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                .ConfigureAwait(false))
            {
                response.EnsureSuccessStatusCode();
                await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
                await using var fileStream = File.Create(tempPath);
                await contentStream.CopyToAsync(fileStream, cancellationToken).ConfigureAwait(false);
            }

            // Download-to-temp-then-move: a crash/cancel mid-download leaves
            // a stray ".download" file, never a truncated file sitting at
            // the real ModelPath that a later run would wrongly treat as
            // already-provisioned via the File.Exists check above.
            File.Move(tempPath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }

        onStatus?.Invoke($"Saved to '{fullPath}'.");
    }
}
