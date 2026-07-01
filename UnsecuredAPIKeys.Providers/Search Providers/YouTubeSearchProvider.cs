using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UnsecuredAPIKeys.Data;
using UnsecuredAPIKeys.Data.Models;
using UnsecuredAPIKeys.Providers._Interfaces;

namespace UnsecuredAPIKeys.Providers.Search_Providers
{
    /// <summary>
    /// Searches YouTube descriptions for exposed API keys.
    /// Tutorial creators often paste API keys in video descriptions.
    /// </summary>
    public class YouTubeSearchProvider(DBContext dbContext, ILogger<YouTubeSearchProvider>? logger = null) : ISearchProvider
    {
        private const string YOUTUBE_SEARCH_URL = "https://www.googleapis.com/youtube/v3/search";
        private const string YOUTUBE_VIDEO_URL = "https://www.googleapis.com/youtube/v3/videos";

        public string ProviderName => "YouTube";

        public async Task<IEnumerable<RepoReference>> SearchAsync(SearchQuery query, SearchProviderToken? token)
        {
            if (query == null || string.IsNullOrWhiteSpace(query.Query))
                throw new ArgumentNullException(nameof(query), "A valid search query is required.");

            // YouTube search requires an API key
            if (token == null || string.IsNullOrWhiteSpace(token.Token))
            {
                logger?.LogWarning("YouTube search requires an API token. Skipping.");
                return [];
            }

            var results = new List<RepoReference>();

            try
            {
                logger?.LogInformation("Starting YouTube search for query: {Query}", query.Query);

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("UnsecuredAPIKeys-Lite/1.0");

                // Search YouTube for videos about API keys
                var searchUrl = $"{YOUTUBE_SEARCH_URL}?part=snippet&q={Uri.EscapeDataString(query.Query + " api key")}&type=video&maxResults=25&key={token.Token}";

                var response = await client.GetAsync(searchUrl);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    logger?.LogWarning("YouTube API returned {StatusCode}.", response.StatusCode);
                    return results;
                }

                using var doc = JsonDocument.Parse(responseBody);
                if (!doc.RootElement.TryGetProperty("items", out var items))
                {
                    logger?.LogInformation("No YouTube results found for query '{Query}'.", query.Query);
                    return results;
                }

                var videoIds = new List<string>();
                foreach (var item in items.EnumerateArray())
                {
                    if (item.TryGetProperty("id", out var idObj) &&
                        idObj.TryGetProperty("videoId", out var videoIdProp))
                    {
                        var videoId = videoIdProp.GetString();
                        if (!string.IsNullOrEmpty(videoId))
                            videoIds.Add(videoId);
                    }
                }

                if (videoIds.Count == 0)
                {
                    query.SearchResultsCount = 0;
                    dbContext.SearchQueries.Update(query);
                    await dbContext.SaveChangesAsync();
                    return results;
                }

                // Get video details including descriptions
                var idsParam = string.Join(",", videoIds);
                var detailsUrl = $"{YOUTUBE_VIDEO_URL}?part=snippet&id={idsParam}&key={token.Token}";

                var detailsResponse = await client.GetAsync(detailsUrl);
                var detailsBody = await detailsResponse.Content.ReadAsStringAsync();

                if (!detailsResponse.IsSuccessStatusCode) return results;

                using var detailsDoc = JsonDocument.Parse(detailsBody);
                if (!detailsDoc.RootElement.TryGetProperty("items", out var videoItems)) return results;

                var videoCount = 0;
                foreach (var video in videoItems.EnumerateArray())
                {
                    if (!video.TryGetProperty("snippet", out var snippet)) continue;

                    var title = snippet.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? "" : "";
                    var description = snippet.TryGetProperty("description", out var dProp) ? dProp.GetString() ?? "" : "";
                    var videoId = video.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                    var channelTitle = snippet.TryGetProperty("channelTitle", out var cProp) ? cProp.GetString() ?? "" : "";

                    // Skip short descriptions
                    if (description.Length < 50) continue;

                    // Check if description contains key patterns
                    if (!ContainsApiKeyPattern(description, query.Query)) continue;

                    results.Add(new RepoReference
                    {
                        SearchQueryId = query.Id,
                        Provider = ProviderName,
                        RepoOwner = channelTitle,
                        RepoName = $"youtube-{videoId}",
                        FilePath = "description.txt",
                        FileURL = $"https://youtube.com/watch?v={videoId}",
                        ApiContentUrl = $"https://youtube.com/watch?v={videoId}",
                        Branch = "main",
                        FileSHA = videoId,
                        FoundUTC = DateTime.UtcNow,
                        RepoURL = $"https://youtube.com/watch?v={videoId}",
                        RepoDescription = title,
                        FileName = "description.txt",
                        _cachedContent = description
                    });

                    videoCount++;
                    if (videoCount >= 20) break;

                    await Task.Delay(TimeSpan.FromMilliseconds(100));
                }

                query.SearchResultsCount = results.Count;
                dbContext.SearchQueries.Update(query);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during YouTube search for query: {Query}", query.Query);
            }

            logger?.LogInformation("Completed YouTube search for '{Query}'. Found {Count} references.", query.Query, results.Count);
            return results;
        }

        private static bool ContainsApiKeyPattern(string content, string query)
        {
            var lower = content.ToLowerInvariant();
            if (!lower.Contains(query.ToLowerInvariant())) return false;

            var patterns = new[] { "api_key", "apikey", "token", "secret", "bearer", "sk-", "AIza", "openai", "anthropic", "replicate", "get your", "get your own", "link in", "description below" };
            return patterns.Any(p => lower.Contains(p, StringComparison.OrdinalIgnoreCase));
        }
    }
}
