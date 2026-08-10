using Looma.Application.UseCases;
using Looma.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Client;

namespace Looma.MCP.Client;

/// <summary>
/// Registers the remote (MCP-client-mode) implementations against the same
/// interfaces <c>Looma.Application.ServiceCollectionExtensions.AddLoomaApplicationUseCases</c>
/// registers the local ones against — a caller (Looma.CLI's composition
/// root) swaps one registration for the other based on
/// <c>Deployment:Mode</c> and otherwise doesn't know or care which is active.
///
/// Takes an already-connected <see cref="McpClient"/> rather than connecting
/// one itself — connecting is async (<see cref="LoomaMcpConnection.ConnectAsync"/>)
/// and must happen before the DI container is built, same as the async
/// startup work Looma.CLI and Looma.MCP.Server already do before their own
/// <c>BuildServiceProvider</c>/<c>Build</c> calls.
///
/// <see cref="RemoteChatUseCase"/> needs an <c>IChatSessionStore</c>
/// registered separately by the caller (e.g.
/// <c>Looma.Infrastructure.LocalStore</c>'s <c>AddLoomaLocalChatStore</c>,
/// called before this) — chat sessions are a local-only concern even in
/// McpClient mode, see <c>IChatCompletionUseCase</c>'s doc comment.
/// <c>SavedAnswerUseCase</c> is registered directly here rather than via a
/// "remote" adapter — it only ever needs the same local
/// <c>ISavedAnswerStore</c>, nothing about it is mode-specific.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddLoomaMcpClientUseCases(this IServiceCollection services, McpClient client)
    {
        services.AddSingleton(client);
        services.AddSingleton<IIndexingUseCase, RemoteIndexingUseCase>();
        services.AddSingleton<ISearchUseCase, RemoteSearchUseCase>();
        services.AddSingleton<IAnswerUseCase, RemoteAnswerUseCase>();
        services.AddSingleton<ICountUseCase, RemoteCountUseCase>();
        services.AddSingleton<IAnswerCache, RemoteAnswerCache>();
        services.AddSingleton<IChatUseCase, RemoteChatUseCase>();
        services.AddSingleton<ISavedAnswerUseCase, SavedAnswerUseCase>();
        services.AddSingleton<ITranscriptionUseCase, RemoteTranscriptionUseCase>();
        services.AddSingleton<IImageCaptionUseCase, RemoteImageCaptionUseCase>();
        services.AddSingleton<IDocumentExtractionUseCase, RemoteDocumentExtractionUseCase>();
        services.AddSingleton<IDocumentExportUseCase, DocumentExportUseCase>();

        return services;
    }
}
