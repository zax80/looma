using Looma.Infrastructure.WebSearch.SearXng;
using Xunit;

namespace Looma.Infrastructure.WebSearch.SearXng.Tests;

/// <summary>
/// Per CLAUDE.md's no-mocks-only rule for Infrastructure.*: tests run
/// against a real local SearXNG instance with its JSON format enabled, not
/// mocks. Pings it once with a real query; if it isn't reachable, or is
/// reachable but returns non-JSON (json format not enabled in its
/// settings.yml — see docs/config-reference.md's WebSearch section), tests
/// report themselves as skipped rather than failing the whole suite.
/// </summary>
public sealed class SearXngFixture : IAsyncLifetime
{
    public HttpClient HttpClient { get; }
    public bool IsAvailable { get; private set; }

    public SearXngFixture()
    {
        var endpoint = Environment.GetEnvironmentVariable("LOOMA_TEST_SEARXNG_ENDPOINT") ?? "http://localhost:8080";
        HttpClient = new HttpClient { BaseAddress = new Uri(endpoint), Timeout = TimeSpan.FromSeconds(5) };
    }

    public SearXngWebSearchProvider CreateProvider() => new(HttpClient);

    public async Task InitializeAsync()
    {
        try
        {
            using var response = await HttpClient.GetAsync("/search?q=test&format=json");
            IsAvailable = response.IsSuccessStatusCode &&
                          response.Content.Headers.ContentType?.MediaType == "application/json";
        }
        catch
        {
            IsAvailable = false;
        }
    }

    public Task DisposeAsync()
    {
        HttpClient.Dispose();
        return Task.CompletedTask;
    }
}
