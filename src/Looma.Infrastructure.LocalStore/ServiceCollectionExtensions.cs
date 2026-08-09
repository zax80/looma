using Looma.Core.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Looma.Infrastructure.LocalStore;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="FileChatSessionStore"/>/<see cref="FileSavedAnswerStore"/>
    /// as <see cref="IChatSessionStore"/>/<see cref="ISavedAnswerStore"/>,
    /// bound from the <c>ChatHistory</c> section of configuration.
    /// </summary>
    public static IServiceCollection AddLoomaLocalChatStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ChatHistoryOptions>().Bind(configuration.GetSection(ChatHistoryOptions.SectionName));

        services.AddSingleton<IChatSessionStore, FileChatSessionStore>();
        services.AddSingleton<ISavedAnswerStore, FileSavedAnswerStore>();

        return services;
    }
}
