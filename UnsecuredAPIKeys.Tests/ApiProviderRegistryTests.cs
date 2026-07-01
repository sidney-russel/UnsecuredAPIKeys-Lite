using UnsecuredAPIKeys.Providers;
using UnsecuredAPIKeys.Providers._Interfaces;

namespace UnsecuredAPIKeys.Tests;

public class ApiProviderRegistryTests
{
    [Fact]
    public void Providers_ReturnsAllRegisteredProviders()
    {
        var providers = ApiProviderRegistry.Providers;
        Assert.NotEmpty(providers);
    }

    [Fact]
    public void ScraperProviders_ContainsOnlyScraperEnabled()
    {
        var providers = ApiProviderRegistry.ScraperProviders;
        Assert.NotEmpty(providers);

        foreach (var provider in providers)
        {
            Assert.NotNull(provider.ProviderName);
            Assert.NotEmpty(provider.ProviderName);
            Assert.NotEmpty(provider.RegexPatterns);
        }
    }

    [Fact]
    public void VerifierProviders_ContainsOnlyVerifierEnabled()
    {
        var providers = ApiProviderRegistry.VerifierProviders;
        Assert.NotEmpty(providers);

        foreach (var provider in providers)
        {
            Assert.NotNull(provider.ProviderName);
            Assert.NotEmpty(provider.ProviderName);
        }
    }

    [Fact]
    public void Providers_AllImplementIApiKeyProvider()
    {
        var providers = ApiProviderRegistry.Providers;
        foreach (var provider in providers)
        {
            Assert.IsAssignableFrom<IApiKeyProvider>(provider);
        }
    }

    [Fact]
    public void Providers_HaveValidApiTypes()
    {
        var providers = ApiProviderRegistry.Providers;
        foreach (var provider in providers)
        {
            Assert.NotEqual(Data.Common.ApiTypeEnum.Unknown, provider.ApiType);
        }
    }

    [Fact]
    public void GetProvidersForBot_Scraper_ReturnsScraperProviders()
    {
        var providers = ApiProviderRegistry.GetProvidersForBot(BotType.Scraper);
        Assert.Equal(ApiProviderRegistry.ScraperProviders, providers);
    }

    [Fact]
    public void GetProvidersForBot_Verifier_ReturnsVerifierProviders()
    {
        var providers = ApiProviderRegistry.GetProvidersForBot(BotType.Verifier);
        Assert.Equal(ApiProviderRegistry.VerifierProviders, providers);
    }

    [Fact]
    public void Providers_ContainsOpenAI()
    {
        var providers = ApiProviderRegistry.Providers;
        Assert.Contains(providers, p => p.ApiType == Data.Common.ApiTypeEnum.OpenAI);
    }

    [Fact]
    public void Providers_ContainsAnthropic()
    {
        var providers = ApiProviderRegistry.Providers;
        Assert.Contains(providers, p => p.ApiType == Data.Common.ApiTypeEnum.AnthropicClaude);
    }

    [Fact]
    public void Providers_ContainsGoogle()
    {
        var providers = ApiProviderRegistry.Providers;
        Assert.Contains(providers, p => p.ApiType == Data.Common.ApiTypeEnum.GoogleAI);
    }
}
