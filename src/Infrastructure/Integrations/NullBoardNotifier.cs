using Application.Abstractions;

namespace Infrastructure.Integrations;

/// <summary>
/// No-op notifier used until the SignalR hub lands in M4. Handlers already publish
/// their change events through this interface, so wiring the real hub is a
/// registration change rather than a rewrite of every handler.
/// </summary>
public sealed class NullBoardNotifier : IBoardNotifier
{
    public Task BoardUpdatedAsync(Guid boardId, object payload,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task MemberChangedAsync(Guid boardId, object payload,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
