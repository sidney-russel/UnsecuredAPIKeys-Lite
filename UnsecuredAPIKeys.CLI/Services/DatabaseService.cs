using Microsoft.EntityFrameworkCore;
using Spectre.Console;
using System.Text.Json;
using UnsecuredAPIKeys.Data;
using UnsecuredAPIKeys.Data.Common;
using UnsecuredAPIKeys.Data.Models;

namespace UnsecuredAPIKeys.CLI.Services;

/// <summary>
/// Service for database initialization and common operations.
/// </summary>
public class DatabaseService(string dbPath = "unsecuredapikeys.db")
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<DBContext> InitializeDatabaseAsync()
    {
        var dbContext = new DBContext(dbPath);

        // Apply migrations (creates database if needed)
        await dbContext.Database.MigrateAsync();

        // Seed default data if needed
        await SeedDefaultDataAsync(dbContext);

        return dbContext;
    }

    private static async Task SeedDefaultDataAsync(DBContext dbContext)
    {
        // Add default search queries if none exist
        if (!await dbContext.SearchQueries.AnyAsync())
        {
            // HIGH VALUE: Targeted queries with GitHub search qualifiers
            // - filename: targets config files where keys actually live
            // - language: targets languages that use env vars
            // - created: targets recent commits (keys more likely valid)
            var defaultQueries = new[]
            {
                // === OpenAI - HIGH VALUE (specific file types, recent) ===
                "sk-proj- filename:.env",
                "sk-proj- filename:config",
                "sk-proj- filename:settings",
                "sk-proj- filename:.env.local",
                "sk-proj- filename:.env.production",
                "OPENAI_API_KEY= filename:.env",
                "OPENAI_API_KEY= filename:config",
                "OPENAI_API_KEY= language:python",
                "OPENAI_API_KEY= language:javascript",
                "openai_api_key= filename:.env",
                "openai.api_key= filename:config",
                "sk-or-v1- filename:.env",
                "sk-or-v1- filename:config",

                // === Anthropic - HIGH VALUE ===
                "sk-ant-api01 filename:.env",
                "sk-ant-api01 filename:config",
                "ANTHROPIC_API_KEY= filename:.env",
                "ANTHROPIC_API_KEY= language:python",
                "ANTHROPIC_API_KEY= language:javascript",
                "anthropic_api_key= filename:.env",
                "claude_api_key= filename:.env",

                // === Google AI - HIGH VALUE ===
                "AIzaSy filename:.env",
                "AIzaSy filename:config",
                "GOOGLE_API_KEY= filename:.env",
                "GOOGLE_API_KEY= language:python",
                "GOOGLE_API_KEY= language:javascript",
                "gemini_api_key= filename:.env",
                "google_api_key= filename:.env",

                // === Docker & CI/CD (keys often leaked in Dockerfiles) ===
                "OPENAI_API_KEY filename:Dockerfile",
                "ANTHROPIC_API_KEY filename:Dockerfile",
                "GOOGLE_API_KEY filename:Dockerfile",
                "OPENAI_API_KEY filename:docker-compose",
                "sk- filename:Dockerfile",
                "sk-ant filename:Dockerfile",
                "AIzaSy filename:Dockerfile",

                // === Env files (highest hit rate) ===
                "OPENAI_API_KEY= filename:.env.example",
                "OPENAI_API_KEY= filename:env.sample",
                "ANTHROPIC_API_KEY= filename:.env.example",
                "GOOGLE_API_KEY= filename:.env.example",
                "api_key= filename:.env created:>=2025-01-01",

                // === GitHub Actions / CI (keys in workflow files) ===
                "OPENAI_API_KEY filename:.github/workflows",
                "ANTHROPIC_API_KEY filename:.github/workflows",
                "GOOGLE_API_KEY filename:.github/workflows",
                "sk- filename:.github/workflows",

                // === Python / Node.js config files ===
                "OPENAI_API_KEY= filename:settings.py",
                "OPENAI_API_KEY= filename:config.py",
                "OPENAI_API_KEY= filename:config.js",
                "OPENAI_API_KEY= filename:config.ts",
                "ANTHROPIC_API_KEY= filename:settings.py",
                "GOOGLE_API_KEY= filename:settings.py",

                // === Generic patterns with high precision ===
                "sk-ant-api01- filename:*.py",
                "sk-ant-api01- filename:*.js",
                "sk-proj- filename:*.py",
                "sk-proj- filename:*.js",

                // === Recent commits only (last 30 days, keys more likely valid) ===
                "OPENAI_API_KEY created:>=2025-06-01",
                "ANTHROPIC_API_KEY created:>=2025-06-01",
                "GOOGLE_API_KEY created:>=2025-06-01",
                "sk-proj- created:>=2025-06-01",
                "sk-ant-api01 created:>=2025-06-01",
                "AIzaSy created:>=2025-06-01",

                // === Fallback broader patterns (lower priority) ===
                "sk-proj-",
                "sk-ant-api",
                "AIzaSy",
                "OPENAI_API_KEY",
                "ANTHROPIC_API_KEY",
                "GOOGLE_API_KEY",
            };

            foreach (var query in defaultQueries)
            {
                dbContext.SearchQueries.Add(new SearchQuery
                {
                    Query = query,
                    IsEnabled = true,
                    LastSearchUTC = DateTime.UtcNow.AddDays(-1)
                });
            }

            await dbContext.SaveChangesAsync();
            AnsiConsole.MarkupLine($"[dim]Added {defaultQueries.Length} default search queries.[/]");
        }
    }

    public static async Task<Statistics> GetStatisticsAsync(DBContext dbContext)
    {
        var stats = new Statistics
        {
            TotalKeys = await dbContext.APIKeys.CountAsync(),
            ValidKeys = await dbContext.APIKeys.CountAsync(k => k.Status == ApiStatusEnum.Valid),
            InvalidKeys = await dbContext.APIKeys.CountAsync(k => k.Status == ApiStatusEnum.Invalid),
            UnverifiedKeys = await dbContext.APIKeys.CountAsync(k => k.Status == ApiStatusEnum.Unverified),
            ValidNoCreditsKeys = await dbContext.APIKeys.CountAsync(k => k.Status == ApiStatusEnum.ValidNoCredits),
            OpenAIKeys = await dbContext.APIKeys.CountAsync(k => k.ApiType == ApiTypeEnum.OpenAI),
            AnthropicKeys = await dbContext.APIKeys.CountAsync(k => k.ApiType == ApiTypeEnum.AnthropicClaude),
            GoogleKeys = await dbContext.APIKeys.CountAsync(k => k.ApiType == ApiTypeEnum.GoogleAI),
            HasGitHubToken = await dbContext.SearchProviderTokens
                .AnyAsync(t => t.IsEnabled && t.SearchProvider == SearchProviderEnum.GitHub)
        };

        return stats;
    }

    public static async Task SaveGitHubTokenAsync(DBContext dbContext, string token)
    {
        var encryptedToken = TokenEncryption.Encrypt(token);

        var existing = await dbContext.SearchProviderTokens
            .FirstOrDefaultAsync(t => t.SearchProvider == SearchProviderEnum.GitHub);

        if (existing != null)
        {
            existing.Token = encryptedToken;
            existing.IsEnabled = true;
        }
        else
        {
            dbContext.SearchProviderTokens.Add(new SearchProviderToken
            {
                Token = encryptedToken,
                SearchProvider = SearchProviderEnum.GitHub,
                IsEnabled = true
            });
        }

        await dbContext.SaveChangesAsync();
    }

    public static async Task<string?> GetDecryptedTokenAsync(DBContext dbContext, SearchProviderEnum provider)
    {
        var tokenEntity = await dbContext.SearchProviderTokens
            .FirstOrDefaultAsync(t => t.IsEnabled && t.SearchProvider == provider);

        if (tokenEntity == null || string.IsNullOrEmpty(tokenEntity.Token))
            return null;

        return TokenEncryption.IsEncrypted(tokenEntity.Token)
            ? TokenEncryption.Decrypt(tokenEntity.Token)
            : tokenEntity.Token;
    }

    public async Task ResetDatabaseAsync()
    {
        var dbContext = new DBContext(dbPath);

        await dbContext.Database.EnsureDeletedAsync();

        dbContext.Dispose();

        if (File.Exists(dbPath))
        {
            File.Delete(dbPath);
        }

        // Remove WAL and SHM files that SQLite may leave behind
        if (File.Exists(dbPath + "-wal")) File.Delete(dbPath + "-wal");
        if (File.Exists(dbPath + "-shm")) File.Delete(dbPath + "-shm");

        // Reinitialize
        await InitializeDatabaseAsync();
    }

    public static async Task ExportKeysAsync(DBContext dbContext, string filePath, bool validOnly, string format)
    {
        var query = dbContext.APIKeys.AsQueryable();

        if (validOnly)
        {
            query = query.Where(k => k.Status == ApiStatusEnum.Valid || k.Status == ApiStatusEnum.ValidNoCredits);
        }

        var keys = await query
            .Include(k => k.References)
            .ToListAsync();

        if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
        {
            await ExportAsJsonAsync(keys, filePath);
        }
        else
        {
            await ExportAsCsvAsync(keys, filePath);
        }
    }

    private static async Task ExportAsJsonAsync(List<APIKey> keys, string filePath)
    {
        var exportData = keys.Select(k => new
        {
            k.Id,
            k.ApiKey,
            Type = k.ApiType.ToString(),
            Status = k.Status.ToString(),
            k.FirstFoundUTC,
            k.LastCheckedUTC,
            Sources = k.References.Select(r => new
            {
                r.RepoURL,
                r.RepoOwner,
                r.RepoName,
                r.FilePath,
                r.FoundUTC
            })
        });

        var json = JsonSerializer.Serialize(exportData, JsonOptions);

        await File.WriteAllTextAsync(filePath, json);
    }

    private static async Task ExportAsCsvAsync(List<APIKey> keys, string filePath)
    {
        var lines = new List<string>
        {
            "Id,ApiKey,Type,Status,FirstFoundUTC,LastCheckedUTC,RepoURL"
        };

        foreach (var key in keys)
        {
            var repoUrl = key.References.FirstOrDefault()?.RepoURL ?? "";
            lines.Add($"{key.Id},\"{key.ApiKey}\",{key.ApiType},{key.Status},{key.FirstFoundUTC:O},{key.LastCheckedUTC:O},\"{repoUrl}\"");
        }

        await File.WriteAllLinesAsync(filePath, lines);
    }
}

public class Statistics
{
    public int TotalKeys { get; set; }
    public int ValidKeys { get; set; }
    public int InvalidKeys { get; set; }
    public int UnverifiedKeys { get; set; }
    public int ValidNoCreditsKeys { get; set; }
    public int OpenAIKeys { get; set; }
    public int AnthropicKeys { get; set; }
    public int GoogleKeys { get; set; }
    public bool HasGitHubToken { get; set; }
}
