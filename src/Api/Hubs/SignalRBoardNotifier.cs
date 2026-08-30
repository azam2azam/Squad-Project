using Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace Api.Hubs;

/// <summary>
/// Pushes Application-layer change events out over SignalR. This is the only place that
/// knows the transport exists — handlers publish through <see cref="IBoardNotifier"/>.
/// </summary>
public sealed class SignalRBoardNotifier(
    IHubContext<BoardsHub> hub,
    ILogger<SignalRBoardNotifier> logger) : IBoardNotifier
{
    public Task BoardUpdatedAsync(Guid boardId, object payload,
        CancellationToken cancellationToken = default)
        => SendAsync(boardId, "BoardUpdated", payload, cancellationToken);

    public Task MemberChangedAsync(Guid boardId, object payload,
        CancellationToken cancellationToken = default)
        => SendAsync(boardId, "MemberChanged", payload, cancellationToken);

    private async Task SendAsync(Guid boardId, string eventName, object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients
                .Group(BoardsHub.GroupFor(boardId))
                .SendAsync(eventName, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            // A broadcast failure must never roll back a write that already succeeded.
            logger.LogWarning(ex, "Failed to broadcast {Event} for board {BoardId}",
                eventName, boardId);
        }
    }
}
