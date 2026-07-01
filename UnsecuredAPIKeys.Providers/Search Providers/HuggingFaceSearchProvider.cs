using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using UnsecuredAPIKeys.Data;
using UnsecuredAPIKeys.Data.Models;
using UnsecuredAPIKeys.Providers._Interfaces;
using UnsecuredAPIKeys.Providers.Exceptions;

namespace UnsecuredAPIKeys.Providers.Search_Providers
{
    /// <summary>
    /// Searches Hugging Face Spaces for exposed API keys.
    /// People paste API keys in app.py, .env, and config files when deploying AI models.
    /// </summary>
    public class HuggingFaceSearchProvider(DBContext dbContext, ILogger<HuggingFaceSearchProvider>? logger = null) : ISearchProvider
    {
        private const string HF_API_BASE = "https://huggingface.co/api";

        public string ProviderName => "Hugging Face";

        public async Task<IEnumerable<RepoReference>> SearchAsync(SearchQuery query, SearchProviderToken? token)
        {
            if (query == null || string.IsNullOrWhiteSpace(query.Query))
                throw new ArgumentNullException(nameof(query), "A valid search query is required.");

            var results = new List<RepoReference>();

            try
            {
                logger?.LogInformation("Starting Hugging Face search for query: {Query}", query.Query);

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("UnsecuredAPIKeys-Lite/1.0");

                if (!string.IsNullOrWhiteSpace(token?.Token))
                {
                    client.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", token.Token);
                }

                // Search HF for models/spaces containing our query
                var searchUrl = $"{HF_API_BASE}/models?search={Uri.EscapeDataString(query.Query)}&limit=50&sort=lastModified&direction=-1";

                var response = await client.GetAsync(searchUrl);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    logger?.LogWarning("Hugging Face API returned {StatusCode}.", response.StatusCode);
                    return results;
                }

                using var doc = JsonDocument.Parse(responseBody);
                if (doc.RootElement.ValueKind != JsonValueKind.Array)
                {
                    logger?.LogInformation("No Hugging Face results found for query '{Query}'.", query.Query);
                    return results;
                }

                var modelCount = 0;
                foreach (var model in doc.RootElement.EnumerateArray())
                {
                    var modelId = model.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                    var modelUrl = model.TryGetProperty("url", out var urlProp) ? urlProp.GetString() ?? "" : "";

                    if (string.IsNullOrEmpty(modelId)) continue;

                    // Fetch model files to look for API keys
                    try
                    {
                        var filesUrl = $"{HF_API_BASE}/models/{modelId}/tree/main";
                        var filesResponse = await client.GetAsync(filesUrl);

                        if (!filesResponse.IsSuccessStatusCode) continue;

                        var filesBody = await filesResponse.Content.ReadAsStringAsync();
                        using var filesDoc = JsonDocument.Parse(filesBody);

                        if (filesDoc.RootElement.ValueKind != JsonValueKind.Array) continue;

                        foreach (var file in filesDoc.RootElement.EnumerateArray())
                        {
                            var fileName = file.TryGetProperty("path", out var pathProp) ? pathProp.GetString() ?? "" : "";
                            var fileSize = file.TryGetProperty("size", out var sizeProp) ? sizeProp.GetInt64() : 0;

                            // Only check config files
                            if (!IsConfigFile(fileName)) continue;
                            if (fileSize > 100_000) continue;

                            // Fetch file content
                            var contentUrl = $"{HF_API_BASE}/models/{modelId}/resolve/main/{fileName}";
                            var contentResponse = await client.GetAsync(contentUrl);

                            if (!contentResponse.IsSuccessStatusCode) continue;

                            var content = await contentResponse.Content.ReadAsStringAsync();
                            if (content.Length > 100_000) continue;

                            // Check if content contains key patterns
                            if (!ContainsApiKeyPattern(content, query.Query)) continue;

                            results.Add(new RepoReference
                            {
                                SearchQueryId = query.Id,
                                Provider = ProviderName,
                                RepoOwner = modelId.Split('/').FirstOrDefault() ?? "",
                                RepoName = modelId,
                                FilePath = fileName,
                                FileURL = $"https://huggingface.co/{modelId}/blob/main/{fileName}",
                                ApiContentUrl = contentUrl,
                                Branch = "main",
                                FileSHA = modelId,
                                FoundUTC = DateTime.UtcNow,
                                RepoURL = $"https://huggingface.co/{modelId}",
                                RepoDescription = $"HF Model: {modelId}",
                                FileName = fileName,
                                _cachedContent = content
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error checking HF model {ModelId}", modelId);
                    }

                    modelCount++;
                    if (modelCount >= 20) break; // Limit per query

                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                }

                query.SearchResultsCount = results.Count;
                dbContext.SearchQueries.Update(query);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during Hugging Face search for query: {Query}", query.Query);
            }

            logger?.LogInformation("Completed Hugging Face search for '{Query}'. Found {Count} references.", query.Query, results.Count);
            return results;
        }

        private static bool IsConfigFile(string fileName)
        {
            var lower = fileName.ToLowerInvariant();
            return lower.EndsWith(".py") || lower.EndsWith(".env") || lower.EndsWith(".yaml") ||
                   lower.EndsWith(".yml") || lower.EndsWith(".json") || lower.EndsWith(".toml") ||
                   lower.EndsWith(".cfg") || lower.EndsWith(".ini") || lower == "dockerfile" ||
                   lower == "requirements.txt";
        }

        private static bool ContainsApiKeyPattern(string content, string query)
        {
            var lower = content.ToLowerInvariant();
            if (!lower.Contains(query.ToLowerInvariant())) return false;

            var patterns = new[] { "api_key", "token", "secret", "bearer", "sk-", "AIza", "openai", "anthropic", "replicate" };
            return patterns.Any(p => lower.Contains(p, StringComparison.OrdinalIgnoreCase));
        }
    }
}
