using UnsecuredAPIKeys.CLI;
using UnsecuredAPIKeys.Data.Common;

namespace UnsecuredAPIKeys.Tests;

public class ConstantsTests
{
    [Fact]
    public void LiteLimits_MaxValidKeys_IsPositive()
    {
        Assert.True(LiteLimits.MaxValidKeys > 0);
    }

    [Fact]
    public void LiteLimits_VerificationDelayMs_IsPositive()
    {
        Assert.True(LiteLimits.VerificationDelayMs > 0);
    }

    [Fact]
    public void LiteLimits_SearchDelayMs_IsPositive()
    {
        Assert.True(LiteLimits.SearchDelayMs > 0);
    }

    [Fact]
    public void LiteLimits_VerificationBatchSize_IsPositive()
    {
        Assert.True(LiteLimits.VerificationBatchSize > 0);
    }

    [Fact]
    public void AppInfo_Name_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Name));
    }

    [Fact]
    public void AppInfo_Version_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.Version));
    }

    [Fact]
    public void AppInfo_DatabaseName_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppInfo.DatabaseName));
    }

    [Fact]
    public void AppInfo_DatabaseName_EndsWithDb()
    {
        Assert.EndsWith(".db", AppInfo.DatabaseName);
    }

    [Fact]
    public void ApiStatusEnum_HasAllValues()
    {
        Assert.True(Enum.IsDefined(typeof(ApiStatusEnum), ApiStatusEnum.Unverified));
        Assert.True(Enum.IsDefined(typeof(ApiStatusEnum), ApiStatusEnum.Valid));
        Assert.True(Enum.IsDefined(typeof(ApiStatusEnum), ApiStatusEnum.Invalid));
        Assert.True(Enum.IsDefined(typeof(ApiStatusEnum), ApiStatusEnum.ValidNoCredits));
        Assert.True(Enum.IsDefined(typeof(ApiStatusEnum), ApiStatusEnum.Error));
    }

    [Fact]
    public void ApiTypeEnum_HasAllLiteProviders()
    {
        Assert.True(Enum.IsDefined(typeof(ApiTypeEnum), ApiTypeEnum.OpenAI));
        Assert.True(Enum.IsDefined(typeof(ApiTypeEnum), ApiTypeEnum.AnthropicClaude));
        Assert.True(Enum.IsDefined(typeof(ApiTypeEnum), ApiTypeEnum.GoogleAI));
    }

    [Fact]
    public void SearchProviderEnum_HasGitHub()
    {
        Assert.True(Enum.IsDefined(typeof(SearchProviderEnum), SearchProviderEnum.GitHub));
    }
}
