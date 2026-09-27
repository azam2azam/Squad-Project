using Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace Api.Hubs;

/// <summary>
/// Pushes new and changed messages to the people with that thread open. The only place
/// that knows the transport exists — handlers publish through
/// <see cref="IMessageNotifier"/>.
/// </summary>
public sealed class SignalRMessageNotifier(
    IHubContext<MessagesHub> hub,
    ILogger<SignalRMessageNotifier> logger) : IMessageNotifier
{
    public Task MessagePostedAsync(Guid conversationId, object payload,
        CancellationToken cancellationToken = default)
        => SendAsync(conversationId, "MessagePosted", payload, cancellationToken);

    public Task MessageChangedAsync(Guid conversationId, object payload,
        CancellationToken cancellationToken = default)
        => SendAsync(conversationId, "MessageChanged", payload, cancellationToken);

    private async Task SendAsync(Guid conversationId, string eventName, object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            await hub.Clients
                .Group(MessagesHub.GroupFor(conversationId))
                .SendAsync(eventName, payload, cancellationToken);
        }
        catch (Exception ex)
        {
            // A failed broadcast must never roll back a message that is already written.
            // The reader's next poll or reload picks it up; losing the message would not
            // be recoverable at all.
            logger.LogWarning(ex, "Failed to broadcast {Event} for conversation {ConversationId}",
                eventName, conversationId);
        }
    }
}
