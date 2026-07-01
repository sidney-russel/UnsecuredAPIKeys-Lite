using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UnsecuredAPIKeys.Data;
using UnsecuredAPIKeys.Data.Models;
using UnsecuredAPIKeys.Providers._Interfaces;

namespace UnsecuredAPIKeys.Providers.Search_Providers
{
    /// <summary>
    /// Searches Reddit for exposed API keys.
    /// People share API keys in posts on r/ChatGPT, r/LocalLLaMA, r/selfhosted, etc.
    /// </summary>
    public class RedditSearchProvider(DBContext dbContext, ILogger<RedditSearchProvider>? logger = null) : ISearchProvider
    {
        private const string REDDIT_SEARCH_URL = "https://www.reddit.com/search.json";

        // Subreddits where API keys are commonly shared
        private static readonly string[] TargetSubreddits =
        [
            "ChatGPT", "OpenAI", "LocalLLaMA", "selfhosted",
            "artificial", "MachineLearning", "Python", "programming",
            "LocalAI", "StableDiffusion", "ComfyUI", "AUTOMATIC1111"
        ];

        public string ProviderName => "Reddit";

        public async Task<IEnumerable<RepoReference>> SearchAsync(SearchQuery query, SearchProviderToken? token)
        {
            if (query == null || string.IsNullOrWhiteSpace(query.Query))
                throw new ArgumentNullException(nameof(query), "A valid search query is required.");

            var results = new List<RepoReference>();

            try
            {
                logger?.LogInformation("Starting Reddit search for query: {Query}", query.Query);

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("UnsecuredAPIKeys-Lite/1.0 (API key security research)");

                // Search Reddit for posts containing our query
                var searchUrl = $"{REDDIT_SEARCH_URL}?q={Uri.EscapeDataString(query.Query)}&sort=new&t=month&limit=50";

                var response = await client.GetAsync(searchUrl);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    logger?.LogWarning("Reddit API returned {StatusCode}.", response.StatusCode);
                    return results;
                }

                using var doc = JsonDocument.Parse(responseBody);
                if (!doc.RootElement.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("children", out var children))
                {
                    logger?.LogInformation("No Reddit results found for query '{Query}'.", query.Query);
                    return results;
                }

                var postCount = 0;
                foreach (var child in children.EnumerateArray())
                {
                    if (!child.TryGetProperty("data", out var postData)) continue;

                    var title = postData.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "";
                    var selftext = postData.TryGetProperty("selftext", out var textProp) ? textProp.GetString() ?? "" : "";
                    var permalink = postData.TryGetProperty("permalink", out var permProp) ? permProp.GetString() ?? "" : "";
                    var subreddit = postData.TryGetProperty("subreddit", out var subProp) ? subProp.GetString() ?? "" : "";

                    // Combine title and body for search
                    var fullContent = $"{title}\n{selftext}";

                    // Skip short posts
                    if (fullContent.Length < 50) continue;

                    // Check if content contains key patterns
                    if (!ContainsApiKeyPattern(fullContent, query.Query)) continue;

                    results.Add(new RepoReference
                    {
                        SearchQueryId = query.Id,
                        Provider = ProviderName,
                        RepoOwner = $"r/{subreddit}",
                        RepoName = $"reddit-{permalink.GetHashCode():X}",
                        FilePath = $"reddit/{subreddit}/post.md",
                        FileURL = $"https://reddit.com{permalink}",
                        ApiContentUrl = $"https://reddit.com{permalink}.json",
                        Branch = "main",
                        FileSHA = permalink.GetHashCode().ToString(),
                        FoundUTC = DateTime.UtcNow,
                        RepoURL = $"https://reddit.com{permalink}",
                        RepoDescription = title,
                        FileName = $"reddit-post.md",
                        _cachedContent = fullContent
                    });

                    postCount++;
                    if (postCount >= 30) break;

                    await Task.Delay(TimeSpan.FromSeconds(2)); // Reddit rate limit: 10 req/min
                }

                query.SearchResultsCount = results.Count;
                dbContext.SearchQueries.Update(query);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during Reddit search for query: {Query}", query.Query);
            }

            logger?.LogInformation("Completed Reddit search for '{Query}'. Found {Count} references.", query.Query, results.Count);
            return results;
        }

        private static bool ContainsApiKeyPattern(string content, string query)
        {
            var lower = content.ToLowerInvariant();
            if (!lower.Contains(query.ToLowerInvariant())) return false;

            var patterns = new[] { "api_key", "apikey", "token", "secret", "bearer", "sk-", "AIza", "openai", "anthropic", "replicate", "here's my", "here is my", "using this" };
            return patterns.Any(p => lower.Contains(p, StringComparison.OrdinalIgnoreCase));
        }
    }
}
