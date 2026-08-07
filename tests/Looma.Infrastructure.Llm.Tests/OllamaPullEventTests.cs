using System.Text.Json;
using Looma.Infrastructure.Llm;
using Xunit;

namespace Looma.Infrastructure.Llm.Tests;

/// <summary>Exercises real NDJSON lines shaped like Ollama's actual /api/pull output.</summary>
public sealed class OllamaPullEventTests
{
    [Fact]
    public void Deserialize_StatusOnlyLine_ParsesStatus()
    {
        var pullEvent = JsonSerializer.Deserialize<OllamaPullEvent>("""{"status":"pulling manifest"}""");

        Assert.NotNull(pullEvent);
        Assert.Equal("pulling manifest", pullEvent.Status);
        Assert.False(pullEvent.IsSuccess);
        Assert.False(pullEvent.IsError);
    }

    [Fact]
    public void Deserialize_ProgressLine_ParsesTotalsAndDescribesPercentage()
    {
        var pullEvent = JsonSerializer.Deserialize<OllamaPullEvent>(
            """{"status":"downloading","digest":"sha256:abc","total":1000,"completed":250}""");

        Assert.NotNull(pullEvent);
        Assert.Equal("qwen3:8b: downloading (25%)", pullEvent.Describe("qwen3:8b"));
    }

    [Fact]
    public void Deserialize_SuccessLine_IsSuccessTrue()
    {
        var pullEvent = JsonSerializer.Deserialize<OllamaPullEvent>("""{"status":"success"}""");

        Assert.NotNull(pullEvent);
        Assert.True(pullEvent.IsSuccess);
    }

    [Fact]
    public void Deserialize_ErrorLine_IsErrorTrueAndDescribesIt()
    {
        var pullEvent = JsonSerializer.Deserialize<OllamaPullEvent>("""{"error":"model not found"}""");

        Assert.NotNull(pullEvent);
        Assert.True(pullEvent.IsError);
        Assert.Equal("qwen3:8b: model not found", pullEvent.Describe("qwen3:8b"));
    }

    [Fact]
    public void Describe_NoTotalYet_FallsBackToStatusOnly()
    {
        var pullEvent = JsonSerializer.Deserialize<OllamaPullEvent>("""{"status":"verifying sha256 digest"}""");

        Assert.NotNull(pullEvent);
        Assert.Equal("qwen3:8b: verifying sha256 digest", pullEvent.Describe("qwen3:8b"));
    }
}
