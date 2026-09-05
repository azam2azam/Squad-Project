namespace Application.Abstractions;

/// <summary>
/// Keeps the in-process role catalogue in step with the database.
///
/// The catalogue is what renders labels and colours on slides, exports and the composition
/// bar. It is loaded once at startup and refreshed after every role change, so an admin
/// adding a role does not have to restart the API to see it applied.
/// </summary>
public interface IRoleCatalog
{
    Task RefreshAsync(CancellationToken cancellationToken = default);
}
