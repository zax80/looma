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

        return services;
    }
}
