using Microsoft.Extensions.Configuration;

namespace Looma.Infrastructure.Llm;

/// <summary>
/// Deliberately a separate, distinctly-named class from
/// <see cref="ServiceCollectionExtensions"/> — that class's name collides
/// (as a bare identifier) with the same-named classes in
/// <c>Looma.Infrastructure.VectorStore.Qdrant</c> and
/// <c>Looma.Application</c> wherever all three are <c>using</c>'d together
/// (namely, the CLI's composition root), and this method isn't an
/// <c>IServiceCollection</c> extension anyway — it needs to run and complete
/// *before* DI registration, not as part of it.
/// </summary>
public static class OllamaStartup
{
    /// <summary>
    /// Makes sure Ollama is actually reachable — and if it isn't, launches it
    /// and pulls whatever models <c>Models.BaseModel</c> /
    /// <c>Models.EmbeddingModel</c> need — before any DI registration tries
    /// to talk to it. <c>Models.VisionModel</c> is also pulled here, but as
    /// a separate, best-effort step (see below). Call this before
    /// <see cref="ServiceCollectionExtensions.AddLoomaChatClient"/> /
    /// <see cref="ServiceCollectionExtensions.AddLoomaEmbeddingGenerator"/> /
    /// <see cref="ServiceCollectionExtensions.AddLoomaImageCaptioner"/>
    /// in standalone mode.
    ///
    /// BaseModel/EmbeddingModel failures are fatal — every RAG operation
    /// genuinely needs both. VisionModel failures are NOT fatal: a real run
    /// hit a bad tag in config.json ("qwen2.5-vl:7b" — a name that doesn't
    /// exist in Ollama's registry — instead of the correct "qwen2.5vl:7b"),
    /// and because the pull was on the same fatal path as Base/Embedding,
    /// it blocked `looma answer` for a plain text question that never
    /// touched vision at all. Same reasoning as
    /// <see cref="LocalModelFileProvisioner"/> treats a failed CLIP download:
    /// vision is only needed for the image-captioning path, so a failure
    /// there shouldn't block everything else. A run that actually needs
    /// captioning will still fail loudly and specifically at that point.
    ///
    /// <see cref="LlmOptions.ImageEmbeddingModel"/> (CLIP) is handled
    /// entirely separately by <see cref="LocalModelFileProvisioner"/> — it's a
    /// local ONNX Runtime session, not something Ollama serves.
    ///
    /// Same allowlist check as the DI registration path: refuses to probe or
    /// launch anything against a non-allowlisted endpoint.
    /// </summary>
    /// <param name="confirmInstall">
    /// Called if the 'ollama' executable can't be found at all, with a
    /// description of the install command that would run. Return true to
    /// actually run it. Pass null (the default) to never install anything —
    /// appropriate for non-interactive contexts.
    /// </param>
    public static async Task EnsureOllamaReadyAsync(
        IConfiguration configuration,
        Action<string>? onStatus = null,
        Func<string, Task<bool>>? confirmInstall = null,
        CancellationToken cancellationToken = default)
    {
        var (llmOptions, securityOptions) = ServiceCollectionExtensions.BindOptions(configuration);
        ServiceCollectionExtensions.ValidateEndpoint(
            llmOptions.BaseModel.Endpoint, securityOptions, $"{LlmOptions.SectionName}:{nameof(LlmOptions.BaseModel)}");
        ServiceCollectionExtensions.ValidateEndpoint(
            llmOptions.EmbeddingModel.Endpoint, securityOptions, $"{LlmOptions.SectionName}:{nameof(LlmOptions.EmbeddingModel)}");

        using var manager = new OllamaLifecycleManager(llmOptions.BaseModel.Endpoint, onStatus, confirmInstall);

        // Fatal: both are needed by every RAG operation.
        await manager.EnsureReadyAsync([llmOptions.BaseModel.Model, llmOptions.EmbeddingModel.Model], cancellationToken)
            .ConfigureAwait(false);

        // Best-effort: only the image-captioning path needs this. By the
        // time this runs, Ollama is already confirmed reachable (the call
        // above ensured that), so this only ever attempts the pull itself.
        try
        {
            ServiceCollectionExtensions.ValidateEndpoint(
                llmOptions.VisionModel.Endpoint, securityOptions, $"{LlmOptions.SectionName}:{nameof(LlmOptions.VisionModel)}");
            await manager.EnsureReadyAsync([llmOptions.VisionModel.Model], cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OllamaLifecycleException or InferenceEndpointNotAllowedException)
        {
            onStatus?.Invoke(
                $"Warning: couldn't prepare the vision model '{llmOptions.VisionModel.Model}' ({ex.Message}). " +
                "Image captioning will fail until this is resolved; everything else is unaffected.");
        }
    }
}
