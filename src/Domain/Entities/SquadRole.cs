using System.Text.RegularExpressions;
using Domain.Common;
using Domain.Enums;

namespace Domain.Entities;

/// <summary>
/// A role a squad member can hold — the values behind the "Default role" list.
///
/// The seven built-ins are seeded from <see cref="RoleMetadata.Defaults"/> and keep the
/// numeric values the <see cref="Role"/> enum names, so every board, person and exported
/// spreadsheet written before roles were configurable still reads correctly. Custom roles
/// take values from <see cref="RoleMetadata.FirstCustomValue"/> upward.
///
/// The primary key is that number, assigned explicitly rather than by the database: it is
/// the value stored on every SquadMember and Person, so it has to be stable and known
/// before insert.
/// </summary>
public class SquadRole
{
    private SquadRole() { }

    public SquadRole(int value, string name, string label, string pluralLabel, string color,
        int orderIndex, bool isBuiltIn = false)
    {
        Value = value;
        SetName(name);
        SetLabel(label);
        SetPluralLabel(pluralLabel);
        SetColor(color);
        OrderIndex = orderIndex;
        IsBuiltIn = isBuiltIn;
        IsActive = true;
    }

    /// <summary>The number stored on members and people. Never reused, never renumbered.</summary>
    public int Value { get; private set; }

    /// <summary>Stable identifier used by imports and the API, e.g. "ScrumMaster".</summary>
    public string Name { get; private set; } = string.Empty;

    public string Label { get; private set; } = string.Empty;

    /// <summary>Used by the composition legend: "2 Scrum Masters".</summary>
    public string PluralLabel { get; private set; } = string.Empty;

    public string Color { get; private set; } = "#8595A9";

    public int OrderIndex { get; private set; }

    /// <summary>One of the original seven. Cannot be removed, only renamed or recoloured.</summary>
    public bool IsBuiltIn { get; private set; }

    /// <summary>
    /// Inactive roles disappear from the pickers but keep rendering wherever they are
    /// already assigned — retiring a role must not blank out historical boards.
    /// </summary>
    public bool IsActive { get; private set; }

    public Role AsRole => (Role)Value;

    public RoleDefinition ToDefinition() =>
        new(AsRole, Name, Label, PluralLabel, Color, OrderIndex);

    public void Update(string label, string pluralLabel, string color, int orderIndex)
    {
        SetLabel(label);
        SetPluralLabel(pluralLabel);
        SetColor(color);
        OrderIndex = orderIndex;
    }

    public void Rename(string name)
    {
        if (IsBuiltIn)
        {
            // The name is what Excel imports and the API match on; changing it for a
            // built-in would break files exported before the change.
            throw new DomainException("A built-in role's identifier cannot be changed.");
        }

        SetName(name);
    }

    public void Deactivate()
    {
        if (IsBuiltIn)
        {
            throw new DomainException(
                $"{Label} is a built-in role and cannot be removed. Rename it instead.");
        }

        IsActive = false;
    }

    public void Reactivate() => IsActive = true;

    private void SetName(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new DomainException("A role requires an identifier.");
        }

        // Letters and digits only: this value travels through spreadsheets and URLs.
        if (!Regex.IsMatch(trimmed, "^[A-Za-z][A-Za-z0-9]*$"))
        {
            throw new DomainException(
                "A role identifier must start with a letter and contain only letters and digits, " +
                "for example ScrumMaster.");
        }

        Name = trimmed;
    }

    private void SetLabel(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
        {
            throw new DomainException("A role requires a name to display.");
        }

        Label = label.Trim();
    }

    private void SetPluralLabel(string pluralLabel) =>
        // Falling back to the singular is kinder than refusing: "DevOps" has no plural.
        PluralLabel = string.IsNullOrWhiteSpace(pluralLabel) ? Label : pluralLabel.Trim();

    private void SetColor(string color)
    {
        var trimmed = color?.Trim() ?? string.Empty;

        if (!Regex.IsMatch(trimmed, "^#[0-9A-Fa-f]{6}$"))
        {
            throw new DomainException(
                "A role colour must be a six-digit hex value, for example #2DD4BF.");
        }

        // Upper case so exported files and the database agree on one spelling.
        Color = trimmed.ToUpperInvariant();
    }
}
