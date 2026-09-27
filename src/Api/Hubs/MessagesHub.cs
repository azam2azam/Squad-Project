using System.Security.Claims;
using Application.Abstractions;
using Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Api.Hubs;

/// <summary>
/// Live messages. A client joins the group for the thread it has open, so a reply reaches
/// the people reading it rather than everyone connected.
///
/// The join is authorised here rather than taken on trust. A group is a broadcast
/// address: anyone who can join one receives everything sent to it, so an unchecked
/// <see cref="JoinConversation"/> would let a caller holding a direct conversation's id
/// read two other people's thread live, without ever passing the authorised GET. Guids
/// being hard to guess is not an access control.
///
/// Identity comes from <see cref="HubCallerContext.User"/> and not from
/// <c>ICurrentUserContext</c>: that one reads <c>IHttpContextAccessor</c>, which is null
/// once the connection is a WebSocket, and would silently deny every join.
/// </summary>
[Authorize]
public sealed class MessagesHub(IAppDbContext db) : Hub
{
    public static string GroupFor(Guid conversationId) => $"conversation:{conversationId}";

    public async Task JoinConversation(Guid conversationId)
    {
        if (!await CanReadAsync(conversationId))
        {
            throw new HubException("You do not have access to that conversation.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(conversationId));
    }

    public Task LeaveConversation(Guid conversationId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupFor(conversationId));

    /// <summary>
    /// The same rule the API applies: channels are open to anyone signed in, a direct
    /// thread only to its two people.
    /// </summary>
    private async Task<bool> CanReadAsync(Guid conversationId)
    {
        var userId = CurrentUserId();
        if (userId is null) return false;

        var conversation = await db.Conversations
            .AsNoTracking()
            .Where(c => c.Id == conversationId)
            .Select(c => new
            {
                c.Kind,
                IsMember = c.Members.Any(m => m.UserId == userId)
            })
            .FirstOrDefaultAsync();

        if (conversation is null) return false;

        return conversation.Kind != ConversationKind.Direct || conversation.IsMember;
    }

    private Guid? CurrentUserId() =>
        Guid.TryParse(
            Context.User?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? Context.User?.FindFirstValue("sub"),
            out var id)
            ? id
            : null;
}
