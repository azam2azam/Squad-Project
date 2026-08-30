namespace Domain.Enums;

/// <summary>
/// Delivery status of a board. Values are stable and persisted; do not renumber.
/// </summary>
public enum BoardStatus
{
    OnTrack = 0,
    AtRisk = 1,
    Blocked = 2,
    InReview = 3,
    Delivered = 4
}
