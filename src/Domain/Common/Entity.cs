namespace Domain.Common;

/// <summary>Base for all persisted aggregates. Identity is a client-generatable GUID.</summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    /// <summary>
    /// Overrides the generated id. Only for deterministic seeding and JSON import,
    /// where an id must be preserved across a round-trip. Refuses to re-key an entity
    /// that has already been assigned a meaningful id by an import.
    /// </summary>
    public void AssignIdForImport(Guid id)
    {
        if (id == Guid.Empty)
        {
            throw new DomainException("Imported id cannot be empty.");
        }

        Id = id;
    }
}

/// <summary>Thrown when an operation would violate a domain invariant.</summary>
public sealed class DomainException(string message) : Exception(message);
