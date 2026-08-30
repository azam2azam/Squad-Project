using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Lightweight change log (spec FR-10): who changed a board field, from what, to what, when.
/// Append-only — entries are never edited or removed.
/// </summary>
public class BoardAuditEntry : Entity
{
    private BoardAuditEntry() { }

    public BoardAuditEntry(Guid boardId, string field, string? oldValue, string? newValue, string changedBy)
    {
        BoardId = boardId;
        Field = field;
        OldValue = oldValue;
        NewValue = newValue;
        ChangedBy = string.IsNullOrWhiteSpace(changedBy) ? "system" : changedBy.Trim();
        ChangedAt = DateTimeOffset.UtcNow;
    }

    public Guid BoardId { get; private set; }

    /// <summary>Logical field name, e.g. "Status", "ProgressPercent", "Members".</summary>
    public string Field { get; private set; } = string.Empty;

    public string? OldValue { get; private set; }
    public string? NewValue { get; private set; }
    public string ChangedBy { get; private set; } = "system";
    public DateTimeOffset ChangedAt { get; private set; }

    /// <summary>Human-readable line for the board history panel.</summary>
    public string Summary => OldValue is null
        ? $"{ChangedBy} set {Field} to {NewValue}"
        : $"{ChangedBy} changed {Field} from {OldValue} to {NewValue}";
}
