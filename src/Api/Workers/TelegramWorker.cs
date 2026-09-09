using Application.Abstractions;
using Application.Telegram;
using MediatR;

namespace Api.Workers;

/// <summary>
/// Holds a long poll open against Telegram and hands each message to the pipeline.
///
/// Separate from <see cref="IntegrationSyncWorker"/> because the shape of the work is the
/// opposite. That one wakes on a schedule to ask Jira a question; this one waits with a
/// connection open, because a status update typed into a phone should land on the board in
/// seconds, not at the top of the next interval.
///
/// The loop is deliberately dull: when Telegram is switched off it sleeps and asks again,
/// so switching it on in the settings screen takes effect without a restart, and a failed
/// poll never ends the loop.
/// </summary>
public sealed class TelegramWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<TelegramWorker> logger) : BackgroundService
{
    /// <summary>How long each poll waits for a message before returning empty.</summary>
    private const int LongPollSeconds = 25;

    /// <summary>How long to wait before asking again when Telegram is not connected.</summary>
    private static readonly TimeSpan IdleDelay = TimeSpan.FromSeconds(30);

    /// <summary>Backoff after a failure, so an outage is not hammered.</summary>
    private static readonly TimeSpan ErrorDelay = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let the API finish starting (and migrations finish running) before the first poll.
        if (!await DelayAsync(TimeSpan.FromSeconds(20), stoppingToken)) return;

        while (!stoppingToken.IsCancellationRequested)
        {
            TimeSpan wait;

            try
            {
                wait = await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // A background loop that dies on one bad poll is worse than one that logs
                // and tries again.
                logger.LogError(ex, "Telegram poll failed; retrying shortly.");
                wait = ErrorDelay;
            }

            if (wait > TimeSpan.Zero && !await DelayAsync(wait, stoppingToken)) return;
        }
    }

    /// <summary>Returns how long to wait before the next poll.</summary>
    private async Task<TimeSpan> PollOnceAsync(CancellationToken stoppingToken)
    {
        // A new scope per poll: the DbContext and settings services are scoped, and a
        // long-lived one would hold stale tracked entities for the life of the process.
        using var scope = scopeFactory.CreateScope();

        var client = scope.ServiceProvider.GetRequiredService<ITelegramClient>();

        if (!await client.IsEnabledAsync(stoppingToken))
        {
            return IdleDelay;
        }

        var sender = scope.ServiceProvider.GetRequiredService<ISender>();
        var report = await sender.Send(new PollTelegramCommand(LongPollSeconds), stoppingToken);

        if (report.Read > 0)
        {
            logger.LogInformation("Telegram poll: {Message}", report.Message);
        }

        // Straight back into the next long poll when something arrived; a short breath
        // otherwise, so an empty portfolio is not polling flat out.
        return report.Read > 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(1);
    }

    private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken token)
    {
        try
        {
            await Task.Delay(delay, token);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
