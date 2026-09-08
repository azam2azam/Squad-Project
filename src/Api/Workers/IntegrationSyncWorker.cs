using Application.Abstractions;
using Application.Integrations;
using MediatR;

namespace Api.Workers;

/// <summary>
/// Runs the Jira and Smartsheet syncs, each on the interval its own admin configured.
///
/// It wakes once a minute and asks the database what the interval is, rather than
/// capturing it at startup — changing the interval on the settings screen takes effect
/// without a restart, which is what an admin expects from a settings screen.
///
/// It does nothing at all unless auto-apply is switched on. That is the deliberate
/// default: Jira offers a suggestion in the board editor and a human accepts it. An admin
/// has to opt in before a background process starts writing to boards on its own.
/// </summary>
public sealed class IntegrationSyncWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<IntegrationSyncWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the API finish starting (and migrations finish running) before the first poll.
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        using var timer = new PeriodicTimer(Tick);

        while (await SafeWaitAsync(timer, stoppingToken))
        {
            try
            {
                await RunIfDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A background loop that dies on one bad tick is worse than one that logs
                // and tries again next minute.
                logger.LogError(ex, "Jira sync tick failed; will retry on the next interval.");
            }
        }
    }

    private async Task RunIfDueAsync(CancellationToken cancellationToken)
    {
        // A new scope per tick: the DbContext and settings services are scoped, and a
        // long-lived one would hold stale tracked entities for the life of the process.
        using var scope = scopeFactory.CreateScope();
        var sender = scope.ServiceProvider.GetRequiredService<ISender>();

        // Each provider keeps its own switch, interval and last-run time, so one being
        // switched off or overdue says nothing about the other. They are run in sequence
        // rather than together: a board could be linked to both, and two writers racing
        // over the same row is not worth the saved second.
        await RunJiraIfDueAsync(scope, sender, cancellationToken);
        await RunSmartsheetIfDueAsync(scope, sender, cancellationToken);
    }

    private async Task RunJiraIfDueAsync(
        IServiceScope scope, ISender sender, CancellationToken cancellationToken)
    {
        var settings = scope.ServiceProvider.GetRequiredService<IJiraSettingsService>();
        var connection = await settings.GetAsync(cancellationToken);

        if (!IsDue(connection.Enabled, connection.AutoApply,
                connection.SyncIntervalMinutes, connection.LastSyncAt))
        {
            return;
        }

        var report = await sender.Send(new SyncBoardsFromJiraCommand(), cancellationToken);
        logger.LogInformation("Scheduled Jira sync: {Message}", report.Message);
    }

    private async Task RunSmartsheetIfDueAsync(
        IServiceScope scope, ISender sender, CancellationToken cancellationToken)
    {
        var settings = scope.ServiceProvider.GetRequiredService<ISmartsheetSettingsService>();
        var connection = await settings.GetAsync(cancellationToken);

        if (!IsDue(connection.Enabled, connection.AutoApply,
                connection.SyncIntervalMinutes, connection.LastSyncAt))
        {
            return;
        }

        var report = await sender.Send(new SyncBoardsFromSmartsheetCommand(), cancellationToken);
        logger.LogInformation("Scheduled Smartsheet sync: {Message}", report.Message);
    }

    private static bool IsDue(bool enabled, bool autoApply, int intervalMinutes, DateTimeOffset? lastSyncAt)
    {
        if (!enabled || !autoApply || intervalMinutes <= 0) return false;

        return lastSyncAt is null
               || DateTimeOffset.UtcNow - lastSyncAt >= TimeSpan.FromMinutes(intervalMinutes);
    }

    /// <summary>
    /// Wraps the timer so shutdown ends the loop quietly instead of throwing through
    /// the host's stop path.
    /// </summary>
    private static async Task<bool> SafeWaitAsync(PeriodicTimer timer, CancellationToken token)
    {
        try
        {
            return await timer.WaitForNextTickAsync(token);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
