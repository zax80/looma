using Looma.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Looma.Infrastructure.WebSearch.DuckDuckGo;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="DuckDuckGoWebSearchProvider"/> as the app's
    /// <see cref="IWebSearchProvider"/>, bound from the <c>WebSearch</c>
    /// section of configuration (see config.json). Always registered — see
    /// <see cref="DuckDuckGoOptions"/>'s doc comment for why that's
    /// harmless as long as <c>RagOptions.EnableWebSearch</c> stays false.
    /// </summary>
    public static IServiceCollection AddDuckDuckGoWebSearch(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DuckDuckGoOptions>()
            .Bind(configuration.GetSection(DuckDuckGoOptions.SectionName));

        services.AddHttpClient<IWebSearchProvider, DuckDuckGoWebSearchProvider>((serviceProvider, client) =>
        {
            var options = configuration.GetSection(DuckDuckGoOptions.SectionName).Get<DuckDuckGoOptions>() ?? new DuckDuckGoOptions();
            client.BaseAddress = new Uri(options.Endpoint);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        return services;
    }
}
