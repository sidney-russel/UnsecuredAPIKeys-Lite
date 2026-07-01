using UnsecuredAPIKeys.Providers._Base;

namespace UnsecuredAPIKeys.Tests;

public class BaseApiKeyProviderTests
{
    private class TestProvider : BaseApiKeyProvider
    {
        public override string ProviderName => "Test";
        public override Data.Common.ApiTypeEnum ApiType => Data.Common.ApiTypeEnum.Unknown;
        public override IEnumerable<string> RegexPatterns => ["test-pattern"];
        protected override Task<Providers.Common.ValidationResult> ValidateKeyWithHttpClientAsync(
            string apiKey, HttpClient httpClient)
        {
            return Task.FromResult(Providers.Common.ValidationResult.Success(System.Net.HttpStatusCode.OK));
        }
    }

    private readonly TestProvider _provider = new();

    [Theory]
    [InlineData("valid-key-12345", true)]
    [InlineData("short", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidKeyFormat_ReturnsExpected(string? key, bool expected)
    {
        var method = typeof(BaseApiKeyProvider).GetMethod("IsValidKeyFormat",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var result = (bool)method.Invoke(_provider, [key!])!;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void CleanApiKey_RemovesBearerPrefix()
    {
        var method = typeof(BaseApiKeyProvider).GetMethod("CleanApiKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var result = (string)method.Invoke(_provider, ["Bearer test-key-12345"])!;
        Assert.Equal("test-key-12345", result);
    }

    [Fact]
    public void CleanApiKey_RemovesXApiKeyPrefix()
    {
        var method = typeof(BaseApiKeyProvider).GetMethod("CleanApiKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var result = (string)method.Invoke(_provider, ["x-api-key:test-key-12345"])!;
        Assert.Equal("test-key-12345", result);
    }

    [Fact]
    public void CleanApiKey_TrimsWhitespace()
    {
        var method = typeof(BaseApiKeyProvider).GetMethod("CleanApiKey",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var result = (string)method.Invoke(_provider, ["  test-key-12345  "])!;
        Assert.Equal("test-key-12345", result);
    }

    [Fact]
    public void GetMaxRetries_ReturnsDefault()
    {
        var method = typeof(BaseApiKeyProvider).GetMethod("GetMaxRetries",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var result = (int)method.Invoke(_provider, [])!;
        Assert.Equal(3, result);
    }

    [Fact]
    public void GetTimeoutSeconds_ReturnsDefault()
    {
        var method = typeof(BaseApiKeyProvider).GetMethod("GetTimeoutSeconds",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        var result = (int)method.Invoke(_provider, [])!;
        Assert.Equal(30, result);
    }

    [Fact]
    public void ContainsAny_WithMatchingText_ReturnsTrue()
    {
        var indicators = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "error", "fail" };
        var method = typeof(BaseApiKeyProvider).GetMethod("ContainsAny",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var result = (bool)method.Invoke(null, ["something failed", indicators])!;
        Assert.True(result);
    }

    [Fact]
    public void ContainsAny_WithNoMatch_ReturnsFalse()
    {
        var indicators = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "error", "fail" };
        var method = typeof(BaseApiKeyProvider).GetMethod("ContainsAny",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var result = (bool)method.Invoke(null, ["something success", indicators])!;
        Assert.False(result);
    }

    [Fact]
    public void TruncateResponse_ShortString_ReturnsOriginal()
    {
        var method = typeof(BaseApiKeyProvider).GetMethod("TruncateResponse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var result = (string)method.Invoke(null, ["hello", 200])!;
        Assert.Equal("hello", result);
    }

    [Fact]
    public void TruncateResponse_LongString_ReturnsTruncated()
    {
        var longString = new string('a', 300);
        var method = typeof(BaseApiKeyProvider).GetMethod("TruncateResponse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var result = (string)method.Invoke(null, [longString, 200])!;
        Assert.Equal(203, result.Length); // 200 + "..."
        Assert.EndsWith("...", result);
    }

    [Fact]
    public void TruncateResponse_NullOrEmpty_ReturnsEmpty()
    {
        var method = typeof(BaseApiKeyProvider).GetMethod("TruncateResponse",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.Equal("", method.Invoke(null, [null!, 200])!);
        Assert.Equal("", method.Invoke(null, ["", 200])!);
    }

    [Fact]
    public void IsSuccessStatusCode_200_ReturnsTrue()
    {
        var method = typeof(BaseApiKeyProvider).GetMethod("IsSuccessStatusCode",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.True((bool)method.Invoke(null, [System.Net.HttpStatusCode.OK])!);
    }

    [Fact]
    public void IsSuccessStatusCode_404_ReturnsFalse()
    {
        var method = typeof(BaseApiKeyProvider).GetMethod("IsSuccessStatusCode",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        Assert.False((bool)method.Invoke(null, [System.Net.HttpStatusCode.NotFound])!);
    }

    [Fact]
    public void QuotaIndicators_NotEmpty()
    {
        var field = typeof(BaseApiKeyProvider).GetField("QuotaIndicators",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var indicators = (HashSet<string>)field.GetValue(null)!;
        Assert.NotEmpty(indicators);
        Assert.Contains("credit", indicators);
        Assert.Contains("quota", indicators);
    }

    [Fact]
    public void UnauthorizedIndicators_NotEmpty()
    {
        var field = typeof(BaseApiKeyProvider).GetField("UnauthorizedIndicators",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var indicators = (HashSet<string>)field.GetValue(null)!;
        Assert.NotEmpty(indicators);
        Assert.Contains("invalid_api_key", indicators);
    }
}
