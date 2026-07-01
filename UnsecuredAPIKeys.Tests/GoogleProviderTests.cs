using System.Text.RegularExpressions;
using UnsecuredAPIKeys.Providers._Base;
using UnsecuredAPIKeys.Providers.AI_Providers;

namespace UnsecuredAPIKeys.Tests;

public class GoogleProviderTests
{
    private readonly GoogleProvider _provider = new();

    [Theory]
    [InlineData("AIzaSy1234567890abcdefghijklmnopqrstuvwx", true)]
    [InlineData("AIzaSyA1234567890abcdefghijklmnopqrstuvw", true)]
    [InlineData("AIzaSy1234567890abcdefghijklmnopq", true)]
    [InlineData("short", false)]
    [InlineData("", false)]
    [InlineData("AIza-something", false)]
    [InlineData("sk-proj-abc", false)]
    public void IsValidKeyFormat_ReturnsExpected(string key, bool expected)
    {
        var method = typeof(BaseApiKeyProvider).GetMethod("IsValidKeyFormat",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var result = (bool)method.Invoke(_provider, [key])!;
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("AIzaSy1234567890abcdefghijklmnopqrstuvwx")]
    [InlineData("AIzaSyA1234567890abcdefghijklmnopqrstuvw")]
    public void RegexPatterns_MatchValidKeys(string key)
    {
        var matched = _provider.RegexPatterns.Any(p =>
            Regex.IsMatch(key, p));
        Assert.True(matched, $"Key '{key}' should match at least one Google regex pattern");
    }

    [Theory]
    [InlineData("sk-proj-abc123")]
    [InlineData("sk-ant-api01-abc123")]
    [InlineData("not-a-key-at-all")]
    public void RegexPatterns_DontMatchInvalidKeys(string key)
    {
        var matched = _provider.RegexPatterns.Any(p =>
            Regex.IsMatch(key, p));
        Assert.False(matched, $"Key '{key}' should NOT match Google regex patterns");
    }

    [Fact]
    public void ProviderName_ReturnsGoogle()
    {
        Assert.Equal("Google", _provider.ProviderName);
    }

    [Fact]
    public void ApiType_ReturnsGoogleAI()
    {
        Assert.Equal(Data.Common.ApiTypeEnum.GoogleAI, _provider.ApiType);
    }

    [Fact]
    public void RegexPatterns_NotEmpty()
    {
        Assert.NotEmpty(_provider.RegexPatterns);
    }
}
