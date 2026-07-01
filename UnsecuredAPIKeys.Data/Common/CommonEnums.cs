namespace UnsecuredAPIKeys.Data.Common
{
    /// <summary>
    /// Search provider for finding API keys.
    /// Lite version: GitHub only.
    /// Full version: www.UnsecuredAPIKeys.com
    /// </summary>
    public enum SearchProviderEnum
    {
        Unknown = -99,
        GitHub = 1
    }

    /// <summary>
    /// Status of an API key in the system.
    /// </summary>
    public enum ApiStatusEnum
    {
        /// <summary>The key was found but not yet checked for validity.</summary>
        Unverified = -99,

        /// <summary>The key was checked and is valid/working.</summary>
        Valid = 1,

        /// <summary>The key was checked and is not working (invalid, expired, revoked, etc.).</summary>
        Invalid = 0,

        /// <summary>The key is valid but has no credits/quota.</summary>
        ValidNoCredits = 7,

        /// <summary>The key was checked and is erroring out for some reason.</summary>
        Error = 6
    }

    /// <summary>
    /// Type of API provider.
    /// Lite version: OpenAI, Anthropic, Google only.
    /// Full version with all providers: www.UnsecuredAPIKeys.com
    /// </summary>
    public enum ApiTypeEnum
    {
        Unknown = -99,

        // AI Services - Lite version only supports these 3
        OpenAI = 100,
        AnthropicClaude = 120,
        GoogleAI = 130
    }
}
