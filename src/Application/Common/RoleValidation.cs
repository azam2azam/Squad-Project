using Domain.Enums;
using FluentValidation;

namespace Application.Common;

public static class RoleValidation
{
    /// <summary>
    /// Roles are configurable, so <c>IsInEnum()</c> is wrong: it would refuse every role an
    /// administrator adds. This checks the live catalogue instead.
    ///
    /// Retired roles are accepted deliberately. They are gone from the pickers, but someone
    /// who already holds one must still be editable — refusing them would make those people
    /// unsaveable until an admin restored a role they meant to retire.
    /// </summary>
    public static IRuleBuilderOptions<T, Role> MustBeAKnownRole<T>(
        this IRuleBuilder<T, Role> rule) =>
        rule.Must(RoleMetadata.IsKnown)
            .WithMessage("'{PropertyName}' is not a role this deployment knows about.");
}
