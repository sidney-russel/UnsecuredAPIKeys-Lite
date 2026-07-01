namespace UnsecuredAPIKeys.CLI;

/// <summary>
/// Constants for the lite version of UnsecuredAPIKeys.
/// Full version available at www.UnsecuredAPIKeys.com
/// </summary>
public static class LiteLimits
{
    /// <summary>
    /// Maximum valid keys for lite version.
    ///
    /// WARNING: If you modify this limit, do NOT publish your database
    /// or results to a public repository. This would expose working API
    /// keys to malicious actors who could abuse them.
    ///
    /// For higher limits, use www.UnsecuredAPIKeys.com
    /// </summary>
    public const int MaxValidKeys = 50;

    /// <summary>
    /// Delay between verification batches (milliseconds).
    /// </summary>
    public const int VerificationDelayMs = 1000;

    /// <summary>
    /// Delay between GitHub search queries (milliseconds).
    /// </summary>
    public const int SearchDelayMs = 5000;

    /// <summary>
    /// Number of keys to process per verification batch.
    /// </summary>
    public const int VerificationBatchSize = 10;
}

/// <summary>
/// Application-wide constants.
/// </summary>
public static class AppInfo
{
    public const string Name = "UnsecuredAPIKeys Lite";
    public const string Version = "1.0.0";
    public const string FullVersionUrl = "www.UnsecuredAPIKeys.com";
    public const string DatabaseName = "unsecuredapikeys.db";
}
