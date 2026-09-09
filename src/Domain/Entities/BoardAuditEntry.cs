using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// Lightweight change log (spec FR-10): who changed a board field, from what, to what, when.
/// Append-only — entries are never edited or removed.
/// </summary>
public class BoardAuditEntry : Entity
{
    private BoardAuditEntry() { }

    public BoardAuditEntry(Guid boardId, string field, string? oldValue, string? newValue,
        string changedBy, string? source = null)
    {
        BoardId = boardId;
        Field = field;
        OldValue = oldValue;
        NewValue = newValue;
        ChangedBy = string.IsNullOrWhiteSpace(changedBy) ? "system" : changedBy.Trim();
        Source = string.IsNullOrWhiteSpace(source) ? null : source.Trim();
        ChangedAt = DateTimeOffset.UtcNow;
    }

    public Guid BoardId { get; private set; }

    /// <summary>Logical field name, e.g. "Status", "ProgressPercent", "Members".</summary>
    public string Field { get; private set; } = string.Empty;

    public string? OldValue { get; private set; }
    public string? NewValue { get; private set; }
    public string ChangedBy { get; private set; } = "system";

    /// <summary>
    /// Where the change came in from, when it was not somebody typing into the app —
    /// "Telegram" today. Null means the editor, which is the overwhelming majority, so the
    /// history stays quiet rather than labelling every ordinary edit.
    ///
    /// Kept apart from <see cref="ChangedBy"/> deliberately: that field is matched exactly
    /// against a person's name to build their activity feed, so decorating it would lose
    /// them their history.
    /// </summary>
    public string? Source { get; private set; }

    public DateTimeOffset ChangedAt { get; private set; }

    /// <summary>Human-readable line for the board history panel.</summary>
    public string Summary
    {
        get
        {
            var change = OldValue is null
                ? $"{ChangedBy} set {Field} to {NewValue}"
                : $"{ChangedBy} changed {Field} from {OldValue} to {NewValue}";

            return Source is null ? change : $"{change} (via {Source})";
        }
    }
}
