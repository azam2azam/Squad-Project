namespace Application.Abstractions;

/// <summary>
/// Fan-out of new and changed messages to people with the thread open.
///
/// Same shape as <see cref="IBoardNotifier"/> and for the same reason: the Application
/// layer publishes events, and only the API layer knows SignalR exists. A chat that needs
/// a refresh to show a reply is not a chat, so this one is not optional in practice — but
/// it is still an interface, because a broadcast failure must never roll back a message
/// that has already been written.
/// </summary>
public interface IMessageNotifier
{
    Task MessagePostedAsync(Guid conversationId, object payload,
        CancellationToken cancellationToken = default);

    /// <summary>An edit or a withdrawal. Carries the whole message, not a patch.</summary>
    Task MessageChangedAsync(Guid conversationId, object payload,
        CancellationToken cancellationToken = default);
}
