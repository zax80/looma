using Looma.MCP.Server.Auth;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Looma.MCP.Server.Tests;

public sealed class ApiKeyAuthorizerTests
{
    private const string ExpectedKey = "correct-horse-battery-staple";

    private static IHeaderDictionary Headers(params (string Key, string Value)[] entries)
    {
        var headers = new HeaderDictionary();
        foreach (var (key, value) in entries)
        {
            headers[key] = value;
        }

        return headers;
    }

    [Fact]
    public void IsAuthorized_CorrectBearerToken_ReturnsTrue()
    {
        var headers = Headers(("Authorization", $"Bearer {ExpectedKey}"));

        Assert.True(ApiKeyAuthorizer.IsAuthorized(headers, ExpectedKey));
    }

    [Fact]
    public void IsAuthorized_CorrectXApiKeyHeader_ReturnsTrue()
    {
        var headers = Headers(("X-Api-Key", ExpectedKey));

        Assert.True(ApiKeyAuthorizer.IsAuthorized(headers, ExpectedKey));
    }

    [Fact]
    public void IsAuthorized_BearerTakesPrecedenceOverXApiKey()
    {
        var headers = Headers(("Authorization", $"Bearer {ExpectedKey}"), ("X-Api-Key", "wrong-key"));

        Assert.True(ApiKeyAuthorizer.IsAuthorized(headers, ExpectedKey));
    }

    [Fact]
    public void IsAuthorized_WrongKey_ReturnsFalse()
    {
        var headers = Headers(("Authorization", "Bearer wrong-key"));

        Assert.False(ApiKeyAuthorizer.IsAuthorized(headers, ExpectedKey));
    }

    [Fact]
    public void IsAuthorized_NoHeaders_ReturnsFalse()
    {
        Assert.False(ApiKeyAuthorizer.IsAuthorized(Headers(), ExpectedKey));
    }

    [Fact]
    public void IsAuthorized_MissingBearerPrefix_ReturnsFalse()
    {
        var headers = Headers(("Authorization", ExpectedKey));

        Assert.False(ApiKeyAuthorizer.IsAuthorized(headers, ExpectedKey));
    }

    [Fact]
    public void IsAuthorized_EmptyBearerToken_ReturnsFalse()
    {
        var headers = Headers(("Authorization", "Bearer "));

        Assert.False(ApiKeyAuthorizer.IsAuthorized(headers, ExpectedKey));
    }

    [Theory]
    [InlineData("bearer")]
    [InlineData("BEARER")]
    [InlineData("Bearer")]
    public void IsAuthorized_BearerPrefixIsCaseInsensitive(string prefix)
    {
        var headers = Headers(("Authorization", $"{prefix} {ExpectedKey}"));

        Assert.True(ApiKeyAuthorizer.IsAuthorized(headers, ExpectedKey));
    }

    [Fact]
    public void IsAuthorized_KeyOfDifferentLength_ReturnsFalse()
    {
        var headers = Headers(("Authorization", $"Bearer {ExpectedKey}extra"));

        Assert.False(ApiKeyAuthorizer.IsAuthorized(headers, ExpectedKey));
    }
}
