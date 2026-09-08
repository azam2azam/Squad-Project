using System.Text.RegularExpressions;
using Domain.Common;

namespace Domain.Entities;

/// <summary>
/// A portfolio-level grouping above the boards — "VIDA 4", "AI", and so on.
///
/// Distinct from <see cref="Board.Product"/>, which names the module a board covers
/// (Discharge, Invoice, OPD UI). A category collects many products under one programme,
/// so the portfolio can be read one programme at a time.
///
/// A board's category is optional. Boards created before categories existed keep working
/// and report as uncategorised rather than being forced into a bucket somebody invented.
/// </summary>
public class BoardCategory : Entity
{
    private BoardCategory() { }

    public BoardCategory(string name, string? description, string color, int orderIndex)
    {
        SetName(name);
        Description = Trim(description);
        SetColor(color);
        OrderIndex = orderIndex;
        IsActive = true;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>Used for the chip on a card and the series colour in analytics.</summary>
    public string Color { get; private set; } = "#8595A9";

    public int OrderIndex { get; private set; }

    /// <summary>
    /// Retiring is soft. The category leaves the pickers, but boards already in it keep
    /// their grouping — otherwise retiring one would silently re-shuffle the portfolio.
    /// </summary>
    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public void Update(string name, string? description, string color, int orderIndex)
    {
        SetName(name);
        Description = Trim(description);
        SetColor(color);
        OrderIndex = orderIndex;
    }

    public void Deactivate() => IsActive = false;

    public void Reactivate() => IsActive = true;

    private void SetName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new DomainException("A category needs a name.");
        }

        if (trimmed.Length > 80)
        {
            throw new DomainException("A category name must be 80 characters or fewer.");
        }

        Name = trimmed;
    }

    private void SetColor(string color)
    {
        var trimmed = color?.Trim() ?? string.Empty;

        if (!Regex.IsMatch(trimmed, "^#[0-9A-Fa-f]{6}$"))
        {
            throw new DomainException(
                "A category colour must be a six-digit hex value, for example #2563EB.");
        }

        // Upper case so exported files and the database agree on one spelling.
        Color = trimmed.ToUpperInvariant();
    }

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
