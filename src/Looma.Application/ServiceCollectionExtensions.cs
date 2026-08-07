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

        return services;
    }
}
