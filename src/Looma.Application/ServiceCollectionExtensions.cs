using Looma.Application.Configuration;
using Looma.Application.UseCases;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Looma.Application;

/// <summary>
/// Registers the concrete use-case implementations against the
/// milestone-1 interfaces. Callers must separately register
/// <c>IVectorStore</c>, <c>IAnswerCache</c>,
/// <c>IEmbeddingGenerator&lt;string, Embedding&lt;float&gt;&gt;</c>,
/// and <c>IChatClient</c> (e.g. via
/// <c>Looma.Infrastructure.VectorStore.Qdrant</c> /
/// <c>Looma.Infrastructure.Llm</c>'s own extensions) — this method only
/// wires the Application-layer orchestration on top of them.
///
/// <see cref="IChatCompletionUseCase"/> is included here — it's stateless
/// generation only (retrieval + prompt + LLM call), needs nothing beyond
/// what every other use case in this list already needs, and both
/// Looma.MCP.Server (as the <c>looma_chat</c> tool) and Standalone mode's
/// <see cref="ChatUseCase"/> depend on it directly.
///
/// <see cref="IChatUseCase"/> and <see cref="ISavedAnswerUseCase"/> are
/// deliberately NOT registered here — see
/// <see cref="AddLoomaLocalChatOrchestration"/>.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLoomaApplicationUseCases(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RagOptions>().Bind(configuration.GetSection(RagOptions.SectionName));
        services.AddOptions<EmbeddingModelOptions>().Bind(configuration.GetSection(EmbeddingModelOptions.SectionName));
        services.AddOptions<ImageEmbeddingModelOptions>().Bind(configuration.GetSection(ImageEmbeddingModelOptions.SectionName));

        services.AddSingleton<IIndexingUseCase, IndexingUseCase>();
        services.AddSingleton<ISearchUseCase, SearchUseCase>();
        services.AddSingleton<IAnswerUseCase, AnswerUseCase>();
        services.AddSingleton<ICountUseCase, CountUseCase>();
        services.AddSingleton<IChatCompletionUseCase, ChatCompletionUseCase>();
        services.AddSingleton<ITranscriptionUseCase, TranscriptionUseCase>();
        services.AddSingleton<IImageCaptionUseCase, ImageCaptionUseCase>();
        services.AddSingleton<IDocumentExtractionUseCase, DocumentExtractionUseCase>();
        services.AddSingleton<IDocumentExportUseCase, DocumentExportUseCase>();

        return services;
    }

    /// <summary>
    /// Local chat-session and saved-answer orchestration —
    /// <see cref="IChatUseCase"/> (needs <c>IChatSessionStore</c> +
    /// <see cref="IChatCompletionUseCase"/>, both already registered) and
    /// <see cref="ISavedAnswerUseCase"/> (needs <c>ISavedAnswerStore</c>).
    /// Callers must register <c>IChatSessionStore</c>/<c>ISavedAnswerStore</c>
    /// separately (e.g. <c>Looma.Infrastructure.LocalStore</c>'s
    /// <c>AddLoomaLocalChatStore</c>) and this method's own
    /// <see cref="IChatCompletionUseCase"/> dependency (via
    /// <see cref="AddLoomaApplicationUseCases"/>) before calling this.
    ///
    /// Split out from <see cref="AddLoomaApplicationUseCases"/> specifically
    /// because Looma.MCP.Server doesn't need this at all — chat sessions
    /// live client-side only (see <see cref="IChatCompletionUseCase"/>'s
    /// doc comment) — while Looma.MCP.Client's McpClient-mode composition
    /// root registers <see cref="IChatUseCase"/> itself (as
    /// <c>RemoteChatUseCase</c>) instead of calling this.
    /// </summary>
    public static IServiceCollection AddLoomaLocalChatOrchestration(this IServiceCollection services)
    {
        services.AddSingleton<IChatUseCase, ChatUseCase>();
        services.AddSingleton<ISavedAnswerUseCase, SavedAnswerUseCase>();

        return services;
    }
}
