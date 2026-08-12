using Looma.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Looma.Infrastructure.WebSearch.SearXng;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="SearXngWebSearchProvider"/> as the app's
    /// <see cref="IWebSearchProvider"/>, bound from the <c>WebSearch</c>
    /// section of configuration (see config.json). Always registered — see
    /// <see cref="SearXngOptions"/>'s doc comment for why an unconfigured
    /// or unreachable endpoint is harmless as long as
    /// <c>RagOptions.EnableWebSearch</c> stays false.
    /// </summary>
    public static IServiceCollection AddSearXngWebSearch(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SearXngOptions>()
            .Bind(configuration.GetSection(SearXngOptions.SectionName));

        services.AddHttpClient<IWebSearchProvider, SearXngWebSearchProvider>((serviceProvider, client) =>
        {
            var options = configuration.GetSection(SearXngOptions.SectionName).Get<SearXngOptions>() ?? new SearXngOptions();
            client.BaseAddress = new Uri(options.Endpoint);
            client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        });

        return services;
    }
}
