using Application.Portability;

namespace Application.Abstractions;

/// <summary>
/// Reads and writes the portfolio as an Excel workbook.
///
/// Behind an interface because the spreadsheet library is an Infrastructure concern —
/// the import and export use-cases work in terms of <see cref="BoardExportFile"/>, which
/// is the same shape the JSON round-trip uses. That means Excel and JSON share one
/// import pipeline and cannot drift apart.
/// </summary>
public interface IWorkbookSerializer
{
    /// <summary>Renders the portfolio as an .xlsx workbook.</summary>
    byte[] Write(BoardExportFile file);

    /// <summary>
    /// Parses an uploaded workbook. Throws <see cref="WorkbookFormatException"/> with a
    /// readable message when the sheets or headers are not what we expect — a person who
    /// edited the file by hand needs to know which cell is wrong, not a stack trace.
    /// </summary>
    BoardExportFile Read(Stream stream);
}

/// <summary>Raised when an uploaded workbook cannot be understood. Maps to 400.</summary>
public sealed class WorkbookFormatException(string message) : Exception(message);
