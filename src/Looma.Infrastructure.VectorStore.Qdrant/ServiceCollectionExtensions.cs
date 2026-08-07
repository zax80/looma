using Looma.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Looma.Infrastructure.VectorStore.Qdrant;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="QdrantVectorStore"/> as the app's <see cref="IVectorStore"/>,
    /// bound from the <c>VectorStore</c> section of configuration (see config.json).
    /// </summary>
    public static IServiceCollection AddQdrantVectorStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<QdrantOptions>()
            .Bind(configuration.GetSection(QdrantOptions.SectionName));

        services.AddHttpClient<IVectorStore, QdrantVectorStore>((serviceProvider, client) =>
        {
            var options = configuration.GetSection(QdrantOptions.SectionName).Get<QdrantOptions>() ?? new QdrantOptions();
            client.BaseAddress = new Uri(options.Endpoint);
            if (!string.IsNullOrEmpty(options.ApiKey))
            {
                client.DefaultRequestHeaders.Add("api-key", options.ApiKey);
            }
        });

        return services;
    }

    /// <summary>
    /// Registers <see cref="QdrantAnswerCache"/> as the app's <see cref="IAnswerCache"/>,
    /// bound from the <c>AnswerCache</c> section of configuration. Reuses
    /// <c>VectorStore.Endpoint</c>/<c>ApiKey</c> — it's the same Qdrant instance,
    /// just a separate collection.
    /// </summary>
    public static IServiceCollection AddQdrantAnswerCache(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AnswerCacheOptions>()
            .Bind(configuration.GetSection(AnswerCacheOptions.SectionName));

        services.AddHttpClient<IAnswerCache, QdrantAnswerCache>((serviceProvider, client) =>
        {
            var vectorStoreOptions = configuration.GetSection(QdrantOptions.SectionName).Get<QdrantOptions>() ?? new QdrantOptions();
            client.BaseAddress = new Uri(vectorStoreOptions.Endpoint);
            if (!string.IsNullOrEmpty(vectorStoreOptions.ApiKey))
            {
                client.DefaultRequestHeaders.Add("api-key", vectorStoreOptions.ApiKey);
            }
        });

        return services;
    }
}
