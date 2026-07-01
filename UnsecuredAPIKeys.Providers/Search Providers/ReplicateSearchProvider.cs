using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UnsecuredAPIKeys.Data;
using UnsecuredAPIKeys.Data.Models;
using UnsecuredAPIKeys.Providers._Interfaces;

namespace UnsecuredAPIKeys.Providers.Search_Providers
{
    /// <summary>
    /// Searches Replicate for exposed API keys.
    /// People paste REPLICATE_API_TOKEN in model deployments and code.
    /// </summary>
    public class ReplicateSearchProvider(DBContext dbContext, ILogger<ReplicateSearchProvider>? logger = null) : ISearchProvider
    {
        private const string REPLICATE_API_BASE = "https://api.replicate.com/v1";

        public string ProviderName => "Replicate";

        public async Task<IEnumerable<RepoReference>> SearchAsync(SearchQuery query, SearchProviderToken? token)
        {
            if (query == null || string.IsNullOrWhiteSpace(query.Query))
                throw new ArgumentNullException(nameof(query), "A valid search query is required.");

            var results = new List<RepoReference>();

            try
            {
                logger?.LogInformation("Starting Replicate search for query: {Query}", query.Query);

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("UnsecuredAPIKeys-Lite/1.0");

                if (!string.IsNullOrWhiteSpace(token?.Token))
                {
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", token.Token);
                }

                // Search Replicate for models
                var searchUrl = $"{REPLICATE_API_BASE}/models?query={Uri.EscapeDataString(query.Query)}&limit=25";

                var response = await client.GetAsync(searchUrl);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    logger?.LogWarning("Replicate API returned {StatusCode}.", response.StatusCode);
                    return results;
                }

                using var doc = JsonDocument.Parse(responseBody);
                if (!doc.RootElement.TryGetProperty("results", out var modelResults))
                {
                    logger?.LogInformation("No Replicate results found for query '{Query}'.", query.Query);
                    return results;
                }

                var modelCount = 0;
                foreach (var model in modelResults.EnumerateArray())
                {
                    var modelName = model.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                    var modelUrl = model.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? "" : "";

                    if (string.IsNullOrEmpty(modelName)) continue;

                    // Check model description for API keys
                    var description = model.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "" : "";

                    if (ContainsApiKeyPattern(description, query.Query))
                    {
                        results.Add(new RepoReference
                        {
                            SearchQueryId = query.Id,
                            Provider = ProviderName,
                            RepoOwner = modelName.Split('/').FirstOrDefault() ?? "",
                            RepoName = modelName,
                            FilePath = "README.md",
                            FileURL = modelUrl,
                            ApiContentUrl = $"{REPLICATE_API_BASE}/models/{modelName}",
                            Branch = "main",
                            FileSHA = modelName,
                            FoundUTC = DateTime.UtcNow,
                            RepoURL = modelUrl,
                            RepoDescription = $"Replicate model: {modelName}",
                            FileName = "README.md",
                            _cachedContent = description
                        });
                    }

                    modelCount++;
                    if (modelCount >= 20) break;

                    await Task.Delay(TimeSpan.FromMilliseconds(200));
                }

                query.SearchResultsCount = results.Count;
                dbContext.SearchQueries.Update(query);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during Replicate search for query: {Query}", query.Query);
            }

            logger?.LogInformation("Completed Replicate search for '{Query}'. Found {Count} references.", query.Query, results.Count);
            return results;
        }

        private static bool ContainsApiKeyPattern(string content, string query)
        {
            var lower = content.ToLowerInvariant();
            if (!lower.Contains(query.ToLowerInvariant())) return false;

            var patterns = new[] { "api_key", "token", "secret", "bearer", "r8_", "replicate", "REPLICATE_API_TOKEN" };
            return patterns.Any(p => lower.Contains(p, StringComparison.OrdinalIgnoreCase));
        }
    }
}
