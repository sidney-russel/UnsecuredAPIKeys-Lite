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
    /// Searches GitLab public projects for exposed API keys.
    /// GitLab has a public search API that indexes all public projects.
    /// </summary>
    public class GitLabSearchProvider(DBContext dbContext, ILogger<GitLabSearchProvider>? logger = null) : ISearchProvider
    {
        private const string GITLAB_API_BASE = "https://gitlab.com/api/v4";

        public string ProviderName => "GitLab";

        public async Task<IEnumerable<RepoReference>> SearchAsync(SearchQuery query, SearchProviderToken? token)
        {
            if (query == null || string.IsNullOrWhiteSpace(query.Query))
                throw new ArgumentNullException(nameof(query), "A valid search query is required.");

            var results = new List<RepoReference>();

            try
            {
                logger?.LogInformation("Starting GitLab search for query: {Query}", query.Query);

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("UnsecuredAPIKeys-Lite/1.0");

                // GitLab uses a private token or OAuth token
                if (!string.IsNullOrWhiteSpace(token?.Token))
                {
                    client.DefaultRequestHeaders.Add("PRIVATE-TOKEN", token.Token);
                }

                // GitLab code search API: GET /projects/:id/search?scope=blobs
                // For global search: GET /search?scope=blobs&search=query
                var searchUrl = $"{GITLAB_API_BASE}/search?scope=blobs&search={Uri.EscapeDataString(query.Query)}&per_page=100";

                var response = await client.GetAsync(searchUrl);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    throw new SearchRateLimitException(
                        DateTimeOffset.UtcNow.AddMinutes(1),
                        "GitLab API rate limit exceeded.");
                }

                if (!response.IsSuccessStatusCode)
                {
                    logger?.LogWarning("GitLab API returned {StatusCode} for search.", response.StatusCode);
                    return results;
                }

                using var doc = JsonDocument.Parse(responseBody);
                if (!doc.RootElement.TryGetProperty("blobs", out var blobs))
                {
                    logger?.LogInformation("No GitLab results found for query '{Query}'.", query.Query);
                    return results;
                }

                var totalCount = doc.RootElement.TryGetProperty("total_count", out var tc) ? tc.GetInt32() : 0;
                query.SearchResultsCount = totalCount;
                dbContext.SearchQueries.Update(query);
                await dbContext.SaveChangesAsync();

                logger?.LogDebug("Found {Count} GitLab blob results for query '{Query}'.", blobs.GetArrayLength(), query.Query);

                foreach (var blob in blobs.EnumerateArray())
                {
                    var filename = blob.TryGetProperty("filename", out var fn) ? fn.GetString() ?? "" : "";
                    var path = blob.TryGetProperty("path", out var pathProp) ? pathProp.GetString() ?? "" : "";
                    var refName = blob.TryGetProperty("ref", out var refProp) ? refProp.GetString() ?? "main" : "main";
                    var data = blob.TryGetProperty("data", out var dataProp) ? dataProp.GetString() ?? "" : "";
                    var projectUrl = blob.TryGetProperty("project_id", out var projId) ? projId.GetInt64() : 0;

                    // Get project info from path
                    var projectPath = blob.TryGetProperty("path", out var pp) ? pp.GetString() ?? "" : "";
                    var parts = projectPath.Split('/');
                    var repoOwner = parts.Length > 0 ? parts[0] : "";
                    var repoName = parts.Length > 1 ? parts[1] : "";

                    // Skip large files
                    if (data.Length > 100_000)
                        continue;

                    // Skip documentation, tests, examples
                    if (IsLowValueFile(filename, path))
                        continue;

                    results.Add(new RepoReference
                    {
                        SearchQueryId = query.Id,
                        Provider = ProviderName,
                        RepoOwner = repoOwner,
                        RepoName = repoName,
                        FilePath = path,
                        FileURL = $"https://gitlab.com/{projectPath}/-/blob/{refName}/{path}",
                        ApiContentUrl = $"https://gitlab.com/api/v4/projects/{projectUrl}/repository/files/{Uri.EscapeDataString(path)}?ref={refName}",
                        Branch = refName,
                        FileSHA = blob.TryGetProperty("id", out var idProp) ? idProp.GetString() : null,
                        FoundUTC = DateTime.UtcNow,
                        RepoURL = $"https://gitlab.com/{projectPath}",
                        RepoDescription = null,
                        FileName = filename,
                        _cachedContent = data
                    });
                }
            }
            catch (SearchRateLimitException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during GitLab search for query: {Query}", query.Query);
            }

            logger?.LogInformation("Completed GitLab search for '{Query}'. Found {Count} file references.", query.Query, results.Count);
            return results;
        }

        private static bool IsLowValueFile(string filename, string path)
        {
            var lowerPath = path.ToLowerInvariant();
            var lowerName = filename.ToLowerInvariant();

            // Skip documentation
            if (lowerName.EndsWith(".md") || lowerName.EndsWith(".rst") || lowerName.EndsWith(".txt"))
                return true;

            // Skip test files
            if (lowerPath.Contains("/test/") || lowerPath.Contains("/tests/") ||
                lowerPath.Contains("/__tests__/") || lowerName.Contains("test_") ||
                lowerName.Contains("_test.") || lowerName.Contains(".test."))
                return true;

            // Skip example/sample files
            if (lowerPath.Contains("/example") || lowerPath.Contains("/sample") ||
                lowerPath.Contains("/demo/") || lowerName.Contains("example") ||
                lowerName.Contains("sample") || lowerName.Contains("demo"))
                return true;

            // Skip lock files
            if (lowerName.EndsWith(".lock") || lowerName.EndsWith("lock.json") ||
                lowerName.EndsWith("yarn.lock") || lowerName.EndsWith("package-lock.json"))
                return true;

            // Skip binary files
            if (lowerName.EndsWith(".png") || lowerName.EndsWith(".jpg") ||
                lowerName.EndsWith(".gif") || lowerName.EndsWith(".ico") ||
                lowerName.EndsWith(".svg") || lowerName.EndsWith(".woff") ||
                lowerName.EndsWith(".ttf") || lowerName.EndsWith(".eot"))
                return true;

            // Skip generated files
            if (lowerName.Contains(".min.js") || lowerName.Contains(".min.css") ||
                lowerName.Contains(".bundle."))
                return true;

            return false;
        }
    }
}
