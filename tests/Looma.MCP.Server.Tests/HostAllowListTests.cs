using Looma.MCP.Server.Auth;
using Xunit;

namespace Looma.MCP.Server.Tests;

public sealed class HostAllowListTests
{
    [Theory]
    [InlineData("localhost")]
    [InlineData("LOCALHOST")]
    [InlineData("127.0.0.1")]
    [InlineData("::1")]
    public void IsAllowed_DefaultAllowList_KnownLoopbackHosts_ReturnsTrue(string host)
    {
        Assert.True(HostAllowList.IsAllowed(host, HostAllowList.Default));
    }

    [Fact]
    public void IsAllowed_UnrecognizedHost_ReturnsFalse()
    {
        Assert.False(HostAllowList.IsAllowed("evil.example.com", HostAllowList.Default));
    }

    [Fact]
    public void IsAllowed_ConfiguredExtraHost_ReturnsTrue()
    {
        var allowedHosts = new[] { "looma.lan" };

        Assert.True(HostAllowList.IsAllowed("looma.lan", allowedHosts));
        Assert.False(HostAllowList.IsAllowed("localhost", allowedHosts));
    }
}
