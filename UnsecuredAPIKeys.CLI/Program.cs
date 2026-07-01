using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using UnsecuredAPIKeys.CLI;
using UnsecuredAPIKeys.CLI.Services;
using UnsecuredAPIKeys.Data;
using UnsecuredAPIKeys.Data.Common;

string[] MainMenuChoices = [
    "1. Start Scraper (search GitHub for keys)",
    "2. Start Verifier (maintain valid keys)",
    "3. View Status",
    "4. Configure Settings",
    "5. Export Keys",
    "6. Exit"
];

string[] ConfigChoices = [
    "1. Set GitHub Token",
    "2. View Current Settings",
    "3. Reset Database",
    "4. Back to Main Menu"
];

string[] ExportFormatChoices = [
    "1. JSON",
    "2. CSV",
    "3. Back to Main Menu"
];

// Auto-create appsettings.json from example if it doesn't exist
var appSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
var exampleSettingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.example.json");
if (!File.Exists(appSettingsPath) && File.Exists(exampleSettingsPath))
{
    File.Copy(exampleSettingsPath, appSettingsPath);
}

// Initialize services
var services = new ServiceCollection();
services.AddLogging(builder => builder
    .SetMinimumLevel(LogLevel.Warning)
    .AddConsole());
services.AddHttpClient();

await using var serviceProvider = services.BuildServiceProvider();
var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();

// Initialize database
var dbService = new DatabaseService(AppInfo.DatabaseName);
DBContext? dbContext = null;

try
{
    dbContext = await dbService.InitializeDatabaseAsync();
}
catch (Exception ex)
{
    AnsiConsole.MarkupLine($"[red]Failed to initialize database: {Markup.Escape(ex.Message)}[/]");
    return;
}

// Display banner
DisplayBanner();

// Main menu loop
var running = true;
while (running)
{
    var choice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[yellow]What would you like to do?[/]")
            .PageSize(10)
            .AddChoices(MainMenuChoices));

    AnsiConsole.WriteLine();

    switch (choice[0])
    {
        case '1':
            await RunScraperAsync(dbContext, httpClientFactory);
            break;
        case '2':
            await RunVerifierAsync(dbContext, httpClientFactory);
            break;
        case '3':
            await ShowStatusAsync(dbContext, dbService);
            break;
        case '4':
            await ConfigureSettingsAsync(dbContext, dbService);
            break;
        case '5':
            await ExportKeysAsync(dbContext, dbService);
            break;
        case '6':
            running = false;
            break;
    }

    if (running)
    {
        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
        AnsiConsole.Clear();
        DisplayBanner();
    }
}

AnsiConsole.MarkupLine("[green]Goodbye![/]");
dbContext?.Dispose();

// === Helper Methods ===

void DisplayBanner()
{
    AnsiConsole.Write(
        new FigletText(AppInfo.Name)
            .LeftJustified()
            .Color(Color.Cyan1));

    AnsiConsole.Write(new Rule("[dim]Lite Version[/]").RuleStyle("grey").LeftJustified());
    AnsiConsole.MarkupLine($"[dim]Full version: [link]{Markup.Escape(AppInfo.FullVersionUrl)}[/][/]");
    AnsiConsole.MarkupLine($"[dim]Valid key limit: [yellow]{LiteLimits.MaxValidKeys}[/][/]");
    AnsiConsole.WriteLine();

    // Educational purpose notice
    var warningPanel = new Panel(
        "[yellow]This tool is for EDUCATIONAL PURPOSES ONLY.[/]\n\n" +
        "If you discover exposed API keys, please help secure them:\n" +
        "  [green]1.[/] Open an issue on the repository to notify the owner\n" +
        "  [green]2.[/] Never use keys for unauthorized access\n" +
        "  [green]3.[/] Do NOT publish your results publicly\n\n" +
        "[dim]Help make the internet more secure by reporting, not exploiting.[/]")
        .Header("[yellow]Educational Use Only[/]")
        .Border(BoxBorder.Rounded)
        .BorderColor(Color.Yellow);

    AnsiConsole.Write(warningPanel);
    AnsiConsole.WriteLine();
}

async Task RunScraperAsync(DBContext db, IHttpClientFactory factory)
{
    AnsiConsole.Write(new Rule("[cyan]GitHub Scraper[/]").RuleStyle("cyan"));
    AnsiConsole.MarkupLine("[dim]Searches GitHub for exposed API keys. Runs continuously.[/]");
    AnsiConsole.MarkupLine("[dim]Press [yellow]Ctrl+C[/] to stop.[/]");
    AnsiConsole.WriteLine();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (s, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        AnsiConsole.MarkupLine("\n[yellow]Stopping scraper...[/]");
    };

    var scraper = new ScraperService(db, factory);
    await scraper.RunAsync(cts.Token);
}

async Task RunVerifierAsync(DBContext db, IHttpClientFactory factory)
{
    AnsiConsole.Write(new Rule("[green]Key Verifier[/]").RuleStyle("green"));
    AnsiConsole.MarkupLine($"[dim]Maintains up to [yellow]{LiteLimits.MaxValidKeys}[/] valid keys.[/]");
    AnsiConsole.MarkupLine("[dim]Re-checks valid keys and verifies new ones as needed.[/]");
    AnsiConsole.MarkupLine("[dim]Press [yellow]Ctrl+C[/] to stop.[/]");
    AnsiConsole.WriteLine();

    using var cts = new CancellationTokenSource();
    Console.CancelKeyPress += (s, e) =>
    {
        e.Cancel = true;
        cts.Cancel();
        AnsiConsole.MarkupLine("\n[yellow]Stopping verifier...[/]");
    };

    var verifier = new VerifierService(db, factory);
    await verifier.RunAsync(cts.Token);
}

async Task ShowStatusAsync(DBContext db, DatabaseService dbService)
{
    AnsiConsole.Write(new Rule("[blue]Current Status[/]").RuleStyle("blue"));

    var stats = await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(Style.Parse("blue"))
        .StartAsync("Loading statistics...", async ctx =>
        {
            return await DatabaseService.GetStatisticsAsync(db);
        });

    // Create status table
    var table = new Table()
        .Border(TableBorder.Rounded)
        .BorderColor(Color.Grey)
        .AddColumn(new TableColumn("[bold]Metric[/]").LeftAligned())
        .AddColumn(new TableColumn("[bold]Value[/]").RightAligned());

    table.AddRow("Total Keys Found", stats.TotalKeys.ToString(System.Globalization.CultureInfo.InvariantCulture));
    table.AddRow("Valid Keys", $"[green]{stats.ValidKeys}[/] / [yellow]{LiteLimits.MaxValidKeys}[/]");
    table.AddRow("Valid (No Credits)", $"[yellow]{stats.ValidNoCreditsKeys}[/]");
    table.AddRow("Invalid Keys", $"[red]{stats.InvalidKeys}[/]");
    table.AddRow("Pending Verification", $"[blue]{stats.UnverifiedKeys}[/]");
    table.AddRow(new Rule().RuleStyle("dim"));
    table.AddRow("OpenAI Keys", stats.OpenAIKeys.ToString(System.Globalization.CultureInfo.InvariantCulture));
    table.AddRow("Anthropic Keys", stats.AnthropicKeys.ToString(System.Globalization.CultureInfo.InvariantCulture));
    table.AddRow("Google Keys", stats.GoogleKeys.ToString(System.Globalization.CultureInfo.InvariantCulture));
    table.AddRow(new Rule().RuleStyle("dim"));
    table.AddRow("Database", $"[dim]{Markup.Escape(AppInfo.DatabaseName)}[/]");
    table.AddRow("GitHub Token", stats.HasGitHubToken ? "[green]Configured[/]" : "[red]Not configured[/]");

    AnsiConsole.Write(table);
}

async Task ConfigureSettingsAsync(DBContext db, DatabaseService dbService)
{
    AnsiConsole.Write(new Rule("[magenta]Configuration[/]").RuleStyle("magenta"));

    var configChoice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[yellow]What would you like to configure?[/]")
            .AddChoices(ConfigChoices));

    switch (configChoice[0])
    {
        case '1':
            await SetGitHubTokenAsync(db, dbService);
            break;
        case '2':
            await ShowCurrentSettingsAsync(db, dbService);
            break;
        case '3':
            await ResetDatabaseAsync(dbService);
            break;
    }
}

async Task SetGitHubTokenAsync(DBContext db, DatabaseService dbService)
{
    AnsiConsole.MarkupLine("[dim]Enter your GitHub Personal Access Token.[/]");
    AnsiConsole.MarkupLine("[dim]Create one at: https:[[//]]github.com[[/]]settings[[/]]tokens[/]");
    AnsiConsole.MarkupLine("[dim]Required scopes: [yellow]public_repo[/] (for searching public repos)[/]");
    AnsiConsole.MarkupLine("[dim]You can add multiple tokens for higher rate limits (30 searches/min per token).[/]");
    AnsiConsole.WriteLine();

    // Check existing tokens
    var existingTokens = await db.SearchProviderTokens
        .Where(t => t.SearchProvider == SearchProviderEnum.GitHub)
        .ToListAsync();

    if (existingTokens.Count > 0)
    {
        AnsiConsole.MarkupLine($"[dim]Current tokens: {existingTokens.Count}[/]");
        var addMore = AnsiConsole.Confirm("[green]Add another token?[/]", true);
        if (!addMore) return;
    }

    var token = AnsiConsole.Prompt(
        new TextPrompt<string>("[green]GitHub Token:[/]")
            .Secret());

    if (string.IsNullOrWhiteSpace(token))
    {
        AnsiConsole.MarkupLine("[red]Token cannot be empty.[/]");
        return;
    }

    // Validate token format
    if (!token.StartsWith("ghp_", StringComparison.Ordinal) && !token.StartsWith("github_pat_", StringComparison.Ordinal))
    {
        var proceed = AnsiConsole.Confirm(
            "[yellow]Token doesn't match expected GitHub token format. Save anyway?[/]",
            false);

        if (!proceed) return;
    }

    // Check for duplicate
    var isDuplicate = existingTokens.Any(t => t.Token == token);

    if (isDuplicate)
    {
        AnsiConsole.MarkupLine("[yellow]This token is already configured.[/]");
        return;
    }

    await DatabaseService.SaveGitHubTokenAsync(db, token);
    AnsiConsole.MarkupLine("[green]GitHub token saved successfully![/]");

    // Show token count
    var tokenCount = existingTokens.Count(t => t.IsEnabled);

    AnsiConsole.MarkupLine($"[dim]Total GitHub tokens: {tokenCount} ({tokenCount * 30} searches/min)[/]");
}

async Task ShowCurrentSettingsAsync(DBContext db, DatabaseService dbService)
{
    var stats = await DatabaseService.GetStatisticsAsync(db);

    var allTokens = await db.SearchProviderTokens
        .Where(t => t.SearchProvider == SearchProviderEnum.GitHub)
        .ToListAsync();

    var tokenCount = allTokens.Count(t => t.IsEnabled);

    var table = new Table()
        .Border(TableBorder.Rounded)
        .BorderColor(Color.Grey)
        .AddColumn("[bold]Setting[/]")
        .AddColumn("[bold]Value[/]");

    var dbPath = Path.Combine(Environment.CurrentDirectory, AppInfo.DatabaseName);
    table.AddRow("Database Path", Markup.Escape(dbPath));
    table.AddRow("GitHub Tokens", tokenCount > 0
        ? $"[green]{tokenCount} configured[/] ({tokenCount * 30} searches/min)"
        : "[red]Not configured[/]");
    table.AddRow("Max Valid Keys", LiteLimits.MaxValidKeys.ToString(System.Globalization.CultureInfo.InvariantCulture));
    table.AddRow("Supported Providers", "OpenAI, Anthropic, Google");
    table.AddRow("Search Sources", "GitHub Code, GitHub Gists, GitLab");

    AnsiConsole.Write(table);
}

async Task ResetDatabaseAsync(DatabaseService dbService)
{
    var confirm = AnsiConsole.Confirm(
        "[red]Are you sure you want to reset the database? All data will be lost![/]",
        false);

    if (!confirm)
    {
        AnsiConsole.MarkupLine("[dim]Database reset cancelled.[/]");
        return;
    }

    var doubleConfirm = AnsiConsole.Confirm(
        "[red]This action is irreversible. Are you absolutely sure?[/]",
        false);

    if (!doubleConfirm)
    {
        AnsiConsole.MarkupLine("[dim]Database reset cancelled.[/]");
        return;
    }

    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(Style.Parse("red"))
        .StartAsync("Resetting database...", async ctx =>
        {
            await dbService.ResetDatabaseAsync();
        });

    AnsiConsole.MarkupLine("[green]Database reset complete.[/]");
}

async Task ExportKeysAsync(DBContext db, DatabaseService dbService)
{
    AnsiConsole.Write(new Rule("[yellow]Export Keys[/]").RuleStyle("yellow"));

    var exportChoice = AnsiConsole.Prompt(
        new SelectionPrompt<string>()
            .Title("[yellow]Export format:[/]")
            .AddChoices(ExportFormatChoices));

    if (exportChoice[0] == '3') return;

    var validOnly = AnsiConsole.Confirm("Export only valid keys?", true);

    var format = exportChoice[0] == '1' ? "json" : "csv";
    var defaultFileName = exportChoice[0] == '1' ? "keys.json" : "keys.csv";
    var fileName = AnsiConsole.Prompt(
        new TextPrompt<string>("[green]Output file name:[/]")
            .DefaultValue(defaultFileName));

    // Sanitize filename to prevent path traversal
    var invalidChars = Path.GetInvalidFileNameChars();
    var sanitizedName = new string(fileName.Where(c => !invalidChars.Contains(c)).ToArray());
    if (string.IsNullOrWhiteSpace(sanitizedName))
    {
        sanitizedName = defaultFileName;
    }

    // Ensure it's in the current directory (no path components)
    sanitizedName = Path.GetFileName(sanitizedName);

    await AnsiConsole.Status()
        .Spinner(Spinner.Known.Dots)
        .SpinnerStyle(Style.Parse("yellow"))
        .StartAsync($"Exporting to {Markup.Escape(sanitizedName)}...", async ctx =>
        {
            await DatabaseService.ExportKeysAsync(db, sanitizedName, validOnly, format);
        });

    AnsiConsole.MarkupLine($"[green]Exported to [bold]{Markup.Escape(fileName)}[/][/]");
}
