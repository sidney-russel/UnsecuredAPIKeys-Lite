using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Spectre.Console;
using UnsecuredAPIKeys.Data;
using UnsecuredAPIKeys.Data.Common;
using UnsecuredAPIKeys.Data.Models;
using UnsecuredAPIKeys.Providers;
using UnsecuredAPIKeys.Providers._Interfaces;

namespace UnsecuredAPIKeys.CLI.Services;

/// <summary>
/// Verifier service that maintains up to 50 valid API keys.
/// When a key becomes invalid, verifies new keys to maintain the limit.
/// Lite version: 50 key cap.
/// Full version: www.UnsecuredAPIKeys.com
/// </summary>
public class VerifierService(
    DBContext dbContext,
    IHttpClientFactory httpClientFactory,
    ILogger<VerifierService>? logger = null)
{
    private readonly IReadOnlyList<IApiKeyProvider> _providers = ApiProviderRegistry.VerifierProviders;
    private CancellationTokenSource? _cancellationTokenSource;

    private int _validCount;
    private int _invalidCount;
    private int _verifiedCount;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        AnsiConsole.MarkupLine("[green]Starting verifier service...[/]");
        AnsiConsole.MarkupLine($"[dim]Target valid keys: [yellow]{LiteLimits.MaxValidKeys}[/][/]");
        AnsiConsole.MarkupLine($"[dim]Loaded {_providers.Count} verification providers[/]");

        foreach (var provider in _providers)
        {
            AnsiConsole.MarkupLine($"  [dim]- {Markup.Escape(provider.ProviderName)}[/]");
        }

        // Run continuously
        while (!_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                await RunVerificationCycleAsync();

                if (_cancellationTokenSource.Token.IsCancellationRequested)
                    break;

                // Wait before next cycle
                AnsiConsole.MarkupLine($"[dim]Waiting {LiteLimits.VerificationDelayMs / 1000}s before next verification cycle...[/]");
                await Task.Delay(LiteLimits.VerificationDelayMs, _cancellationTokenSource.Token);

                // Reset counters
                _validCount = 0;
                _invalidCount = 0;
                _verifiedCount = 0;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine($"[red]Error during verification: {Markup.Escape(ex.Message)}[/]");
                logger?.LogError(ex, "Verification cycle error");
                await Task.Delay(5000, _cancellationTokenSource.Token);
            }
        }

        AnsiConsole.MarkupLine("[green]Verifier stopped.[/]");
    }

    private async Task RunVerificationCycleAsync()
    {
        // Count current valid keys
        var currentValidCount = await dbContext.APIKeys
            .CountAsync(k => k.Status == ApiStatusEnum.Valid, _cancellationTokenSource!.Token);

        AnsiConsole.MarkupLine($"[dim]Current valid keys: [yellow]{currentValidCount}[/] / [yellow]{LiteLimits.MaxValidKeys}[/][/]");

        // Always re-verify stale valid keys (not checked in last 30 minutes)
        var staleCount = await dbContext.APIKeys
            .CountAsync(k => k.Status == ApiStatusEnum.Valid &&
                           (k.LastCheckedUTC == null || k.LastCheckedUTC < DateTime.UtcNow.AddMinutes(-30)),
                       _cancellationTokenSource!.Token);

        if (staleCount > 0)
        {
            AnsiConsole.MarkupLine($"[dim]Re-verifying {staleCount} stale valid keys...[/]");
            await ReVerifyExistingKeysAsync();
        }

        if (currentValidCount < LiteLimits.MaxValidKeys)
        {
            // Verify unverified keys until we reach the limit
            await VerifyNewKeysAsync(LiteLimits.MaxValidKeys - currentValidCount);
        }

        // Display summary
        var table = new Table()
            .Border(TableBorder.Rounded)
            .AddColumn("[bold]Metric[/]")
            .AddColumn("[bold]Value[/]");

        table.AddRow("Keys Verified", _verifiedCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
        table.AddRow("Now Valid", $"[green]{_validCount}[/]");
        table.AddRow("Now Invalid", $"[red]{_invalidCount}[/]");

        var newValidCount = await dbContext.APIKeys
            .CountAsync(k => k.Status == ApiStatusEnum.Valid, _cancellationTokenSource!.Token);
        table.AddRow("Total Valid", $"[yellow]{newValidCount}[/] / [yellow]{LiteLimits.MaxValidKeys}[/]");

        AnsiConsole.Write(table);
    }

    private async Task ReVerifyExistingKeysAsync()
    {
        AnsiConsole.MarkupLine("[dim]Re-verifying existing valid keys...[/]");

        // Get stale valid keys (not checked in last 30 minutes) or oldest checked
        var keysToReVerify = await dbContext.APIKeys
            .Where(k => k.Status == ApiStatusEnum.Valid &&
                       (k.LastCheckedUTC == null || k.LastCheckedUTC < DateTime.UtcNow.AddMinutes(-30)))
            .OrderBy(k => k.LastCheckedUTC)
            .Take(LiteLimits.VerificationBatchSize)
            .ToListAsync(_cancellationTokenSource!.Token);

        // If no stale keys, re-verify oldest ones
        if (keysToReVerify.Count == 0)
        {
            keysToReVerify = await dbContext.APIKeys
                .Where(k => k.Status == ApiStatusEnum.Valid)
                .OrderBy(k => k.LastCheckedUTC)
                .Take(LiteLimits.VerificationBatchSize)
                .ToListAsync(_cancellationTokenSource!.Token);
        }

        // Re-verify in parallel (3 concurrent to avoid rate limits)
        var semaphore = new System.Threading.SemaphoreSlim(3);

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Re-verifying keys[/]", maxValue: keysToReVerify.Count);

                var tasks = keysToReVerify.Select(async key =>
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                        return;

                    await semaphore.WaitAsync(_cancellationTokenSource.Token);
                    try
                    {
                        await VerifyKeyAsync(key);
                        task.Increment(1);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            });

        await dbContext.SaveChangesAsync(_cancellationTokenSource.Token);
    }

    private async Task VerifyNewKeysAsync(int neededCount)
    {
        AnsiConsole.MarkupLine($"[dim]Verifying unverified keys (need {neededCount} more valid)...[/]");

        // Get unverified keys - prioritize recent ones (more likely valid)
        var keysToVerify = await dbContext.APIKeys
            .Where(k => k.Status == ApiStatusEnum.Unverified)
            .OrderByDescending(k => k.FirstFoundUTC) // Recent keys first
            .Take(Math.Max(neededCount * 3, LiteLimits.VerificationBatchSize))
            .ToListAsync(_cancellationTokenSource!.Token);

        if (keysToVerify.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No unverified keys available.[/]");
            return;
        }

        // Verify keys in parallel (5 concurrent) for speed
        var semaphore = new System.Threading.SemaphoreSlim(5);
        var validFound = 0;
        var verifiedCount = 0;

        await AnsiConsole.Progress()
            .StartAsync(async ctx =>
            {
                var task = ctx.AddTask("[green]Verifying new keys[/]", maxValue: keysToVerify.Count);

                var tasks = keysToVerify.Select(async key =>
                {
                    if (_cancellationTokenSource.Token.IsCancellationRequested)
                        return;

                    // Stop if we've reached our target
                    if (validFound >= neededCount)
                        return;

                    await semaphore.WaitAsync(_cancellationTokenSource.Token);
                    try
                    {
                        var wasValid = await VerifyKeyAsync(key);
                        if (wasValid)
                            Interlocked.Increment(ref validFound);

                        Interlocked.Increment(ref verifiedCount);
                        task.Increment(1);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                });

                await Task.WhenAll(tasks);
            });

        await dbContext.SaveChangesAsync(_cancellationTokenSource.Token);
        AnsiConsole.MarkupLine($"[dim]Verified {verifiedCount} keys, found {validFound} valid.[/]");
    }

    private async Task<bool> VerifyKeyAsync(APIKey key)
    {
        Interlocked.Increment(ref _verifiedCount);

        // Build list of providers to try, starting with the assigned one
        var providersToTry = GetProvidersToTry(key);

        if (providersToTry.Count == 0)
        {
            key.Status = ApiStatusEnum.Error;
            key.LastCheckedUTC = DateTime.UtcNow;
            AnsiConsole.MarkupLine($"[yellow]No matching providers for key[/]");
            return false;
        }

        // Try each matching provider until one succeeds
        foreach (var provider in providersToTry)
        {
            try
            {
                var result = await provider.ValidateKeyAsync(key.ApiKey, httpClientFactory);
                key.LastCheckedUTC = DateTime.UtcNow;

                switch (result.Status)
                {
                    case Providers.Common.ValidationAttemptStatus.Valid:
                        // Update the key's API type if a different provider validated it
                        if (key.ApiType != provider.ApiType)
                        {
                            AnsiConsole.MarkupLine($"[dim]Reclassified from {key.ApiType} to {provider.ApiType}[/]");
                            key.ApiType = provider.ApiType;
                        }
                        key.Status = ApiStatusEnum.Valid;
                        key.ErrorCount = 0;
                        Interlocked.Increment(ref _validCount);
                        AnsiConsole.MarkupLine($"[green]Valid: {Markup.Escape(provider.ProviderName)} key[/]");
                        return true;

                    case Providers.Common.ValidationAttemptStatus.HttpError:
                        // Check if it's a quota/credits issue based on detail
                        if (result.Detail?.Contains("quota", StringComparison.OrdinalIgnoreCase) == true ||
                            result.Detail?.Contains("credit", StringComparison.OrdinalIgnoreCase) == true ||
                            result.Detail?.Contains("billing", StringComparison.OrdinalIgnoreCase) == true)
                        {
                            // Update the key's API type if a different provider validated it
                            if (key.ApiType != provider.ApiType)
                            {
                                AnsiConsole.MarkupLine($"[dim]Reclassified from {key.ApiType} to {provider.ApiType}[/]");
                                key.ApiType = provider.ApiType;
                            }
                            key.Status = ApiStatusEnum.ValidNoCredits;
                            key.ErrorCount = 0;
                            Interlocked.Increment(ref _validCount);
                            AnsiConsole.MarkupLine($"[yellow]Valid [[no credits]]: {Markup.Escape(provider.ProviderName)} key[/]");
                            return true;
                        }
                        // HTTP error but not quota - try next provider
                        continue;

                    case Providers.Common.ValidationAttemptStatus.Unauthorized:
                        // This provider explicitly rejected it - try next provider
                        continue;

                    case Providers.Common.ValidationAttemptStatus.NetworkError:
                        // Network error - don't try other providers, just increment error count
                        key.ErrorCount++;
                        if (key.ErrorCount >= 3)
                        {
                            key.Status = ApiStatusEnum.Error;
                        }
                        return false;

                    default:
                        // Provider-specific error - try next provider
                        continue;
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Error verifying key {KeyId} with provider {Provider}", key.Id, provider.ProviderName);
                // Continue to next provider on exception
                continue;
            }
        }

        // All providers failed - mark as invalid
        key.Status = ApiStatusEnum.Invalid;
        Interlocked.Increment(ref _invalidCount);
        return false;
    }

    /// <summary>
    /// Gets providers to try for a key, ordered by: assigned provider first, then other matching providers.
    /// </summary>
    private List<IApiKeyProvider> GetProvidersToTry(APIKey key)
    {
        var result = new List<IApiKeyProvider>();

        // First, add the assigned provider (if it exists)
        var assignedProvider = _providers.FirstOrDefault(p => p.ApiType == key.ApiType);
        if (assignedProvider != null)
        {
            result.Add(assignedProvider);
        }

        // Then add other providers whose patterns match this key
        foreach (var provider in _providers)
        {
            // Skip the already-added assigned provider
            if (provider.ApiType == key.ApiType)
                continue;

            // Check if any of this provider's patterns match the key
            foreach (var regex in provider.CompiledRegexes)
            {
                if (regex.IsMatch(key.ApiKey))
                {
                    result.Add(provider);
                    break; // One match is enough for this provider
                }
            }
        }

        return result;
    }
}
