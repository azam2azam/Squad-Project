using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Api.Hubs;

/// <summary>
/// Live board updates. Viewers join a per-board group so an edit is pushed only to the
/// people actually looking at that board, rather than broadcast to everyone connected.
/// </summary>
[Authorize]
public sealed class BoardsHub : Hub
{
    /// <summary>Group name for a board. Kept in one place so the hub and notifier agree.</summary>
    public static string GroupFor(Guid boardId) => $"board:{boardId}";

    public Task JoinBoard(Guid boardId) =>
        Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(boardId));

    public Task LeaveBoard(Guid boardId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(boardId));
}
