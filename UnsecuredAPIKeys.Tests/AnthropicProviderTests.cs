using System.Text.RegularExpressions;
using UnsecuredAPIKeys.Providers._Base;
using UnsecuredAPIKeys.Providers.AI_Providers;

namespace UnsecuredAPIKeys.Tests;

public class AnthropicProviderTests
{
    private readonly AnthropicProvider _provider = new();

    [Theory]
    [InlineData("sk-ant-api01-abc123def456ghi789jkl012mno345pqr678", true)]
    [InlineData("sk-ant-abc123def456ghi789jkl012mno345pqr678stu901", true)]
    [InlineData("sk-ant-v1-abc123def456ghi789jkl012mno345pqr678", true)]
    [InlineData("sk-ant-api012-abcdefghijklmnopqrstuvwxyz1234567890ABCDEFG", true)]
    [InlineData("short", false)]
    [InlineData("", false)]
    [InlineData("sk-proj-something", false)]
    [InlineData("sk-ant-", false)]
    public void IsValidKeyFormat_ReturnsExpected(string key, bool expected)
    {
        var method = typeof(BaseApiKeyProvider).GetMethod("IsValidKeyFormat",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var result = (bool)method.Invoke(_provider, [key])!;
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("sk-ant-api01-abc123def456ghi789jkl012mno345pqr678stu901vwx")]
    [InlineData("sk-ant-abc123def456ghi789jkl012mno345pqr678stu901vwx")]
    [InlineData("sk-ant-v1-abc123def456ghi789jkl012mno345pqr678")]
    public void RegexPatterns_MatchValidKeys(string key)
    {
        var matched = _provider.RegexPatterns.Any(p =>
            Regex.IsMatch(key, p));
        Assert.True(matched, $"Key '{key}' should match at least one Anthropic regex pattern");
    }

    [Theory]
    [InlineData("sk-proj-abc123")]
    [InlineData("AIzaSy1234567890abcdefghijklmnopqrstuv")]
    [InlineData("not-a-key-at-all")]
    public void RegexPatterns_DontMatchInvalidKeys(string key)
    {
        var matched = _provider.RegexPatterns.Any(p =>
            Regex.IsMatch(key, p));
        Assert.False(matched, $"Key '{key}' should NOT match Anthropic regex patterns");
    }

    [Fact]
    public void ProviderName_ReturnsAnthropic()
    {
        Assert.Equal("Anthropic", _provider.ProviderName);
    }

    [Fact]
    public void ApiType_ReturnsAnthropicClaude()
    {
        Assert.Equal(Data.Common.ApiTypeEnum.AnthropicClaude, _provider.ApiType);
    }

    [Fact]
    public void RegexPatterns_NotEmpty()
    {
        Assert.NotEmpty(_provider.RegexPatterns);
    }
}
