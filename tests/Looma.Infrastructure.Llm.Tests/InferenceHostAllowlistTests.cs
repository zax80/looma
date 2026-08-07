using Looma.Infrastructure.Llm;
using Xunit;

namespace Looma.Infrastructure.Llm.Tests;

/// <summary>
/// Pure logic, no external service required — unlike the Qdrant integration
/// suite, this one genuinely runs anywhere `dotnet test` runs.
/// </summary>
public sealed class InferenceHostAllowlistTests
{
    private static readonly string[] DefaultAllowlist =
    [
        "localhost", "127.0.0.1", "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16"
    ];

    [Theory]
    [InlineData("http://localhost:11434")]
    [InlineData("http://127.0.0.1:11434")]
    [InlineData("http://10.1.2.3:11434")]
    [InlineData("http://172.16.0.1:11434")]
    [InlineData("http://172.31.255.254:11434")]
    [InlineData("http://192.168.1.50:11434")]
    public void IsAllowed_ReturnsTrue_ForLocalOrPrivateEndpoints(string endpoint)
    {
        Assert.True(InferenceHostAllowlist.IsAllowed(new Uri(endpoint), DefaultAllowlist));
    }

    [Theory]
    [InlineData("http://8.8.8.8:11434")]
    [InlineData("http://api.openai.com")]
    [InlineData("http://172.32.0.1:11434")] // just outside 172.16.0.0/12
    [InlineData("http://11.0.0.1:11434")] // just outside 10.0.0.0/8
    public void IsAllowed_ReturnsFalse_ForNonAllowlistedEndpoints(string endpoint)
    {
        Assert.False(InferenceHostAllowlist.IsAllowed(new Uri(endpoint), DefaultAllowlist));
    }

    [Fact]
    public void IsAllowed_HostMatch_IsCaseInsensitive()
    {
        Assert.True(InferenceHostAllowlist.IsAllowed(new Uri("http://LOCALHOST:11434"), DefaultAllowlist));
    }

    [Fact]
    public void IsAllowed_ReturnsFalse_ForEmptyAllowlist()
    {
        Assert.False(InferenceHostAllowlist.IsAllowed(new Uri("http://localhost:11434"), []));
    }

    [Fact]
    public void IsAllowed_ReturnsTrue_AtTheTopOfAnAllowedRange()
    {
        // 10.0.0.0/8 covers up to 10.255.255.255 inclusive.
        Assert.True(InferenceHostAllowlist.IsAllowed(new Uri("http://10.255.255.255:11434"), DefaultAllowlist));
    }

    [Theory]
    [InlineData("10.0.0.0/33")] // prefix longer than an IPv4 address has bits
    [InlineData("not-a-cidr/nope")] // not parseable as an IP at all
    public void IsAllowed_MalformedCidrEntry_IsIgnoredRatherThanThrowing(string malformedEntry)
    {
        Assert.False(InferenceHostAllowlist.IsAllowed(new Uri("http://10.1.2.3:11434"), [malformedEntry]));
    }
}
