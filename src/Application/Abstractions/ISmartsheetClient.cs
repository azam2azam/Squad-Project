using Domain.Enums;

namespace Application.Abstractions;

/// <summary>
/// Read-only Smartsheet access. Config-gated: when Smartsheet is not configured the
/// implementation reports disabled and the UI hides the sync affordance.
/// </summary>
public interface ISmartsheetClient
{
    /// <summary>Whether a usable connection exists.</summary>
    Task<bool> IsEnabledAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a sheet and summarises it. Never writes to Smartsheet, and never writes to
    /// the board — the caller shows the suggestion and lets a human accept it.
    /// </summary>
    Task<SmartsheetSnapshot?> GetSnapshotAsync(string sheetId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A suggestion derived from a sheet, presented for acceptance rather than applied.
///
/// <paramref name="ProgressFromColumn"/> says whether the percentage came from a real
/// "% Complete" column or was inferred from row statuses, because those are different
/// levels of confidence and the rationale shown to a PO should say which.
/// </summary>
public sealed record SmartsheetSnapshot(
    string SheetName,
    int DoneRows,
    int TotalRows,
    int BlockedRows,
    int SuggestedProgressPercent,
    bool ProgressFromColumn,
    BoardStatus SuggestedStatus,
    string Rationale);
