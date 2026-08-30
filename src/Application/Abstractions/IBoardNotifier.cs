namespace Application.Abstractions;

/// <summary>
/// Fan-out of board changes to connected viewers. Implemented over SignalR in the API
/// layer so the Application layer stays transport-agnostic and testable.
/// </summary>
public interface IBoardNotifier
{
    Task BoardUpdatedAsync(Guid boardId, object payload, CancellationToken cancellationToken = default);

    Task MemberChangedAsync(Guid boardId, object payload, CancellationToken cancellationToken = default);
}
