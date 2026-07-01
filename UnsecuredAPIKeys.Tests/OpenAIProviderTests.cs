using System.Text.RegularExpressions;
using UnsecuredAPIKeys.Providers._Base;
using UnsecuredAPIKeys.Providers.AI_Providers;

namespace UnsecuredAPIKeys.Tests;

public class OpenAIProviderTests
{
    private readonly OpenAIProvider _provider = new();

    [Theory]
    [InlineData("sk-proj-abc123def456ghi789jkl012", true)]
    [InlineData("sk-or-v1-abc123def456ghi789jkl012mno345", true)]
    [InlineData("sk-svcacct-abc123def456ghi789jkl012", true)]
    [InlineData("sk-abc123def456ghi789jkl012mno345pqr678stu901", true)]
    [InlineData("Bearer sk-proj-abc123def456ghi789jkl012", true)]
    [InlineData("short", false)]
    [InlineData("", false)]
    [InlineData("pk-something", false)]
    [InlineData("sk-", false)]
    public void IsValidKeyFormat_ReturnsExpected(string key, bool expected)
    {
        var method = typeof(BaseApiKeyProvider).GetMethod("IsValidKeyFormat",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var result = (bool)method.Invoke(_provider, [key])!;
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("sk-proj-abc123def456ghi789jkl012mno")]
    [InlineData("sk-abc123def456ghi789jkl012mno345pqr678stu901vwx")]
    [InlineData("sk-svcacct-abc123def456ghi789jkl012")]
    public void RegexPatterns_MatchValidKeys(string key)
    {
        var matched = _provider.RegexPatterns.Any(p =>
            Regex.IsMatch(key, p));
        Assert.True(matched, $"Key '{key}' should match at least one OpenAI regex pattern");
    }

    [Theory]
    [InlineData("AIzaSy1234567890abcdefghijklmnopqrstuv")]
    [InlineData("sk-ant-api01-abc123")]
    [InlineData("not-a-key-at-all")]
    public void RegexPatterns_DontMatchInvalidKeys(string key)
    {
        var matched = _provider.RegexPatterns.Any(p =>
            Regex.IsMatch(key, p));
        Assert.False(matched, $"Key '{key}' should NOT match OpenAI regex patterns");
    }

    [Fact]
    public void ProviderName_ReturnsOpenAI()
    {
        Assert.Equal("OpenAI", _provider.ProviderName);
    }

    [Fact]
    public void ApiType_ReturnsOpenAI()
    {
        Assert.Equal(Data.Common.ApiTypeEnum.OpenAI, _provider.ApiType);
    }

    [Fact]
    public void RegexPatterns_NotEmpty()
    {
        Assert.NotEmpty(_provider.RegexPatterns);
    }

    [Fact]
    public void CleanApiKey_RemovesBearerPrefix()
    {
        var method = typeof(BaseApiKeyProvider).GetMethod("CleanApiKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var result = (string)method.Invoke(_provider, ["Bearer sk-proj-test123"])!;
        Assert.Equal("sk-proj-test123", result);
    }

    [Fact]
    public void CleanApiKey_RemovesXApiKeyPrefix()
    {
        var method = typeof(BaseApiKeyProvider).GetMethod("CleanApiKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var result = (string)method.Invoke(_provider, ["x-api-key:sk-proj-test123"])!;
        Assert.Equal("sk-proj-test123", result);
    }

    [Fact]
    public void CleanApiKey_TrimsWhitespace()
    {
        var method = typeof(BaseApiKeyProvider).GetMethod("CleanApiKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var result = (string)method.Invoke(_provider, ["  sk-proj-test123  "])!;
        Assert.Equal("sk-proj-test123", result);
    }
}
