using UnsecuredAPIKeys.CLI.Services;

namespace UnsecuredAPIKeys.Tests;

public class DatabaseServiceTests : IDisposable
{
    private readonly string _testDbPath;
    private readonly DatabaseService _service;

    public DatabaseServiceTests()
    {
        _testDbPath = Path.Combine(Path.GetTempPath(), $"test_{Guid.NewGuid():N}.db");
        _service = new DatabaseService(_testDbPath);
    }

    public void Dispose()
    {
        if (File.Exists(_testDbPath))
            File.Delete(_testDbPath);
    }

    [Fact]
    public async Task InitializeDatabaseAsync_CreatesDatabaseFile()
    {
        var db = await _service.InitializeDatabaseAsync();
        Assert.True(File.Exists(_testDbPath));
        db.Dispose();
    }

    [Fact]
    public async Task InitializeDatabaseAsync_SeedsDefaultQueries()
    {
        var db = await _service.InitializeDatabaseAsync();
        Assert.True(db.SearchQueries.Any());
        Assert.True(db.SearchQueries.Count() >= 10);
        db.Dispose();
    }

    [Fact]
    public async Task InitializeDatabaseAsync_CallsTwice_DoesNotDuplicateQueries()
    {
        var db1 = await _service.InitializeDatabaseAsync();
        var count1 = db1.SearchQueries.Count();
        db1.Dispose();

        var db2 = await _service.InitializeDatabaseAsync();
        var count2 = db2.SearchQueries.Count();
        db2.Dispose();

        Assert.Equal(count1, count2);
    }

    [Fact]
    public async Task GetStatisticsAsync_ReturnsZeroCounts_ForEmptyDb()
    {
        var db = await _service.InitializeDatabaseAsync();
        var stats = await DatabaseService.GetStatisticsAsync(db);

        Assert.Equal(0, stats.TotalKeys);
        Assert.Equal(0, stats.ValidKeys);
        Assert.Equal(0, stats.InvalidKeys);
        Assert.Equal(0, stats.UnverifiedKeys);
        Assert.False(stats.HasGitHubToken);
        db.Dispose();
    }

    [Fact]
    public async Task SaveGitHubTokenAsync_SavesToken()
    {
        var db = await _service.InitializeDatabaseAsync();
        await DatabaseService.SaveGitHubTokenAsync(db, "ghp_test12345678901234567890");

        var hasToken = db.SearchProviderTokens.Any(t =>
            t.IsEnabled && t.Token == "ghp_test12345678901234567890");
        Assert.True(hasToken);
        db.Dispose();
    }

    [Fact]
    public async Task SaveGitHubTokenAsync_UpdatesExistingToken()
    {
        var db = await _service.InitializeDatabaseAsync();
        await DatabaseService.SaveGitHubTokenAsync(db, "ghp_old_token");
        await DatabaseService.SaveGitHubTokenAsync(db, "ghp_new_token");

        var tokens = db.SearchProviderTokens
            .Where(t => t.Token.StartsWith("ghp_"))
            .ToList();
        Assert.Single(tokens);
        Assert.Equal("ghp_new_token", tokens[0].Token);
        db.Dispose();
    }

    [Fact]
    public async Task ResetDatabaseAsync_DeletesAndRecreatesDb()
    {
        var db = await _service.InitializeDatabaseAsync();
        await DatabaseService.SaveGitHubTokenAsync(db, "ghp_test");
        db.Dispose();

        await _service.ResetDatabaseAsync();

        var db2 = await _service.InitializeDatabaseAsync();
        Assert.False(db2.SearchProviderTokens.Any(t => t.Token == "ghp_test"));
        Assert.True(db2.SearchQueries.Any()); // Seeds again
        db2.Dispose();
    }

    [Fact]
    public async Task ExportKeysAsync_EmptyDb_WritesJsonFile()
    {
        var db = await _service.InitializeDatabaseAsync();
        var exportPath = Path.Combine(Path.GetTempPath(), $"export_{Guid.NewGuid():N}.json");

        try
        {
            await DatabaseService.ExportKeysAsync(db, exportPath, false, "json");
            Assert.True(File.Exists(exportPath));
            var content = await File.ReadAllTextAsync(exportPath);
            Assert.Contains("[]", content); // Empty array
        }
        finally
        {
            if (File.Exists(exportPath)) File.Delete(exportPath);
            db.Dispose();
        }
    }

    [Fact]
    public async Task ExportKeysAsync_EmptyDb_WritesCsvFile()
    {
        var db = await _service.InitializeDatabaseAsync();
        var exportPath = Path.Combine(Path.GetTempPath(), $"export_{Guid.NewGuid():N}.csv");

        try
        {
            await DatabaseService.ExportKeysAsync(db, exportPath, false, "csv");
            Assert.True(File.Exists(exportPath));
            var lines = await File.ReadAllLinesAsync(exportPath);
            Assert.Single(lines); // Only header
            Assert.Contains("Id", lines[0]);
        }
        finally
        {
            if (File.Exists(exportPath)) File.Delete(exportPath);
            db.Dispose();
        }
    }
}
