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
    /// Searches Stack Overflow for exposed API keys in code snippets.
    /// Users often paste full API keys when asking for help.
    /// </summary>
    public class StackOverflowSearchProvider(DBContext dbContext, ILogger<StackOverflowSearchProvider>? logger = null) : ISearchProvider
    {
        private const string API_BASE = "https://api.stackexchange.com/2.3";

        public string ProviderName => "Stack Overflow";

        public async Task<IEnumerable<RepoReference>> SearchAsync(SearchQuery query, SearchProviderToken? token)
        {
            if (query == null || string.IsNullOrWhiteSpace(query.Query))
                throw new ArgumentNullException(nameof(query), "A valid search query is required.");

            var results = new List<RepoReference>();

            try
            {
                logger?.LogInformation("Starting Stack Overflow search for query: {Query}", query.Query);

                using var client = new HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("UnsecuredAPIKeys-Lite/1.0");
                client.DefaultRequestHeaders.Accept.Add(
                    new MediaTypeWithQualityHeaderValue("application/json"));

                // Search SO for questions/answers containing our query
                var searchUrl = $"{API_BASE}/search/advanced?order=desc&sort=creation&q={Uri.EscapeDataString(query.Query)}&site=stackoverflow&filter=withbody&pagesize=50";

                var response = await client.GetAsync(searchUrl);
                var responseBody = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    logger?.LogWarning("Stack Overflow API returned {StatusCode}.", response.StatusCode);
                    return results;
                }

                using var doc = JsonDocument.Parse(responseBody);
                if (!doc.RootElement.TryGetProperty("items", out var items))
                {
                    logger?.LogInformation("No Stack Overflow results found for query '{Query}'.", query.Query);
                    return results;
                }

                var totalCount = doc.RootElement.TryGetProperty("total", out var tc) ? tc.GetInt32() : 0;
                query.SearchResultsCount = totalCount;
                dbContext.SearchQueries.Update(query);
                await dbContext.SaveChangesAsync();

                logger?.LogDebug("Found {Count} SO results for query '{Query}'.", items.GetArrayLength(), query.Query);

                foreach (var item in items.EnumerateArray())
                {
                    var questionId = item.TryGetProperty("question_id", out var qid) ? qid.GetInt64() : 0;
                    var title = item.TryGetProperty("title", out var titleProp) ? titleProp.GetString() ?? "" : "";
                    var body = item.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() ?? "" : "";
                    var link = item.TryGetProperty("link", out var linkProp) ? linkProp.GetString() ?? "" : "";
                    var isAnswer = item.TryGetProperty("is_answer", out var ansProp) && ansProp.GetBoolean();

                    // Skip short posts
                    if (body.Length < 50)
                        continue;

                    // Check if body contains key patterns
                    if (!ContainsApiKeyPattern(body, query.Query))
                        continue;

                    results.Add(new RepoReference
                    {
                        SearchQueryId = query.Id,
                        Provider = ProviderName,
                        RepoOwner = "stackoverflow",
                        RepoName = $"question-{questionId}",
                        FilePath = isAnswer ? $"answers/{questionId}" : $"questions/{questionId}",
                        FileURL = link,
                        ApiContentUrl = $"{API_BASE}/questions/{questionId}?site=stackoverflow&filter=withbody",
                        Branch = "main",
                        FileSHA = questionId.ToString(),
                        FoundUTC = DateTime.UtcNow,
                        RepoURL = link,
                        RepoDescription = title,
                        FileName = $"so-{questionId}.md",
                        _cachedContent = body
                    });
                }

                // Also check answers for the top questions
                foreach (var item in items.EnumerateArray().Take(10))
                {
                    var questionId = item.TryGetProperty("question_id", out var qid) ? qid.GetInt64() : 0;
                    if (questionId == 0) continue;

                    try
                    {
                        var answersUrl = $"{API_BASE}/questions/{questionId}/answers?order=desc&sort=votes&site=stackoverflow&filter=withbody&pagesize=5";
                        var answersResponse = await client.GetAsync(answersUrl);
                        var answersBody = await answersResponse.Content.ReadAsStringAsync();

                        if (!answersResponse.IsSuccessStatusCode) continue;

                        using var answersDoc = JsonDocument.Parse(answersBody);
                        if (!answersDoc.RootElement.TryGetProperty("items", out var answers)) continue;

                        foreach (var answer in answers.EnumerateArray())
                        {
                            var answerBody = answer.TryGetProperty("body", out var abProp) ? abProp.GetString() ?? "" : "";
                            var answerId = answer.TryGetProperty("answer_id", out var aidProp) ? aidProp.GetInt64() : 0;
                            var answerLink = answer.TryGetProperty("link", out var alProp) ? alProp.GetString() ?? "" : "";

                            if (answerBody.Length < 50) continue;
                            if (!ContainsApiKeyPattern(answerBody, query.Query)) continue;

                            results.Add(new RepoReference
                            {
                                SearchQueryId = query.Id,
                                Provider = ProviderName,
                                RepoOwner = "stackoverflow",
                                RepoName = $"question-{questionId}",
                                FilePath = $"answers/{answerId}",
                                FileURL = answerLink,
                                ApiContentUrl = $"{API_BASE}/answers/{answerId}?site=stackoverflow&filter=withbody",
                                Branch = "main",
                                FileSHA = answerId.ToString(),
                                FoundUTC = DateTime.UtcNow,
                                RepoURL = answerLink,
                                RepoDescription = $"Answer on SO question {questionId}",
                                FileName = $"so-answer-{answerId}.md",
                                _cachedContent = answerBody
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        logger?.LogWarning(ex, "Error fetching answers for SO question {QuestionId}", questionId);
                    }

                    await Task.Delay(TimeSpan.FromMilliseconds(500)); // SO rate limit: 30 requests/day without key
                }
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "Error during Stack Overflow search for query: {Query}", query.Query);
            }

            logger?.LogInformation("Completed Stack Overflow search for '{Query}'. Found {Count} references.", query.Query, results.Count);
            return results;
        }

        private static bool ContainsApiKeyPattern(string content, string query)
        {
            var lower = content.ToLowerInvariant();

            if (!lower.Contains(query.ToLowerInvariant()))
                return false;

            // Look for key assignment patterns
            var patterns = new[] { "api_key", "apikey", "token", "secret", "bearer", "authorization", "sk-", "AIza", "ghp_" };
            return patterns.Any(p => lower.Contains(p, StringComparison.OrdinalIgnoreCase));
        }
    }
}
