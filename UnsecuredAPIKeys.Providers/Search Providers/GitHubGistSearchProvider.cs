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
    /// Searches GitHub Gists for exposed API keys using the GitHub REST API.
    /// Gists are public, often contain pasted keys/configs, and content is fetched inline.
    /// </summary>
    public class GitHubGistSearchProvider(DBContext dbContext, ILogger<GitHubGistSearchProvider>? logger = null) : ISearchProvider
    {
        public string ProviderName => "GitHub Gists";

        public async Task<IEnumerable<RepoReference>> SearchAsync(SearchQuery query, SearchProviderToken? token)
        {
            if (token == null || string.IsNullOrWhiteSpace(token.Token))
                throw new ArgumentNullException(nameof(token), "A valid GitHub token is required.");

            if (query == null || string.IsNullOrWhiteSpace(query.Query))
                throw new ArgumentNullException(nameof(query), "A valid search query is required.");

            var results = new List<RepoReference>();

            try
            {
                logger?.LogInformation("Starting GitHub Gist search for query: {Query}", query.Query);

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("UnsecuredAPIKeys-Lite/1.0");
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", token.Token);
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                // Use GitHub code search API which also searches gists
                var searchUrl = $"https://api.github.com/search/code?q={Uri.EscapeDataString(query.Query)}+in:file+fork:true&per_page=100";

                var response = await client.GetAsync(searchUrl);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    if (responseBody.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
                    {
                        var resetHeader = response.Headers.Contains("X-RateLimit-Reset")
                            ? response.Headers.GetValues("X-RateLimit-Reset").FirstOrDefault()
                            : null;

                        if (long.TryParse(resetHeader, out var resetUnix))
                        {
                            var resetTime = DateTimeOffset.FromUnixTimeSeconds(resetUnix);
                            var delay = resetTime - DateTimeOffset.UtcNow;
                            if (delay > TimeSpan.Zero && delay.TotalMinutes <= 1)
                                await Task.Delay(delay);
                        }

                        throw new SearchRateLimitException(
                            DateTimeOffset.UtcNow.AddMinutes(1),
                            "GitHub API rate limit exceeded during gist search.");
                    }

                    logger?.LogWarning("GitHub API returned 403 for gist search.");
                    return results;
                }

                if (!response.IsSuccessStatusCode)
                {
                    logger?.LogWarning("GitHub API returned {StatusCode} for gist search.", response.StatusCode);
                    return results;
                }

                using var doc = JsonDocument.Parse(responseBody);
                if (!doc.RootElement.TryGetProperty("items", out var items))
                {
                    logger?.LogInformation("No gist results found for query '{Query}'.", query.Query);
                    return results;
                }

                var totalCount = doc.RootElement.TryGetProperty("total_count", out var tc) ? tc.GetInt32() : 0;
                query.SearchResultsCount = totalCount;
                dbContext.SearchQueries.Update(query);
                await dbContext.SaveChangesAsync();

                logger?.LogDebug("Found {Count} results for query '{Query}'.", items.GetArrayLength(), query.Query);

                foreach (var item in items.EnumerateArray())
                {
                    // Get repository info to check if it's a gist
                    var repositoryUrl = "";
                    if (item.TryGetProperty("repository", out var repo) && repo.TryGetProperty("html_url", out var rUrl))
                        repositoryUrl = rUrl.GetString() ?? "";

                    var htmlUrl = item.TryGetProperty("html_url", out var html) ? html.GetString() ?? "" : "";

                    // Only process gist results
                    var isGist = htmlUrl.Contains("gist.github.com", StringComparison.OrdinalIgnoreCase) ||
                                 repositoryUrl.Contains("gist.github.com", StringComparison.OrdinalIgnoreCase);

                    if (!isGist)
                        continue;

                    var gistId = item.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? "" : "";
                    var filePath = item.TryGetProperty("path", out var pathProp) ? pathProp.GetString() ?? "" : "";

                    // Fetch gist content
                    try
                    {
                        var gistUrl = $"https://api.github.com/gists/{gistId}";
                        var gistResponse = await client.GetAsync(gistUrl);
                        if (!gistResponse.IsSuccessStatusCode)
                            continue;

                        var gistBody = await gistResponse.Content.ReadAsStringAsync();
                        using var gistDoc = JsonDocument.Parse(gistBody);

                        if (!gistDoc.RootElement.TryGetProperty("files", out var files))
                            continue;

                        foreach (var file in files.EnumerateObject())
                        {
                            var fileName = file.Name;
                            var fileObj = file.Value;

                            var size = fileObj.TryGetProperty("size", out var sizeProp) ? sizeProp.GetInt32() : 0;
                            if (size > 100_000)
                                continue;

                            var content = fileObj.TryGetProperty("content", out var contentProp)
                                ? contentProp.GetString() ?? ""
                                : "";

                            if (string.IsNullOrEmpty(content))
                                continue;

                            var owner = gistDoc.RootElement.TryGetProperty("owner", out var ownerProp) &&
                                        ownerProp.TryGetProperty("login", out var loginProp)
                                ? loginProp.GetString() ?? "anonymous"
                                : "anonymous";

                            results.Add(new RepoReference
                            {
                                SearchQueryId = query.Id,
                                Provider = ProviderName,
                                RepoOwner = owner,
                                RepoName = $"gist-{gistId}",
                                FilePath = fileName,
                                FileURL = htmlUrl,
                                ApiContentUrl = gistUrl,
                                Branch = "master",
                                FileSHA = gistId,
                                FoundUTC = DateTime.UtcNow,
                                RepoURL = htmlUrl,
                                RepoDescription = gistDoc.RootElement.TryGetProperty("description", out var descProp)
                                    ? descProp.GetString()
                                    : null,
                                FileName = fileName,
                                _cachedContent = content
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error fetching gist content for {GistId}", gistId);
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(500));
                }
            }
            catch (SearchRateLimitException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during GitHub Gist search for query: {Query}", query.Query);
            }

            logger?.LogInformation("Completed Gist search for '{Query}'. Found {Count} file references.", query.Query, results.Count);
            return results;
        }
    }
}
