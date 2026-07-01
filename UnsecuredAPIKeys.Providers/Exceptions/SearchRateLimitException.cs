namespace UnsecuredAPIKeys.Providers.Exceptions;

/// <summary>
/// Thrown when a search provider's rate limit is exceeded and the wait time is too long.
/// </summary>
public class SearchRateLimitException : Exception
{
    public DateTimeOffset ResetTime { get; }

    public SearchRateLimitException(DateTimeOffset resetTime)
        : base($"GitHub API rate limit exceeded. Resets at {resetTime:O}.")
    {
        ResetTime = resetTime;
    }

    public SearchRateLimitException(DateTimeOffset resetTime, string message)
        : base(message)
    {
        ResetTime = resetTime;
    }
}
