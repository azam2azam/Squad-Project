using Domain.Common;

namespace Domain.Entities;

/// <summary>Something that carries a position in a list its owner controls.</summary>
public interface IOrdered
{
    void SetOrder(int orderIndex);
}

/// <summary>
/// One skill on a person's profile.
///
/// A row rather than a comma-separated string on <see cref="Person"/>, because the point of
/// recording skills is to search across them — "who here knows FHIR" is the question this
/// exists to answer, and you cannot index the inside of a text field usefully.
/// </summary>
public class PersonSkill : Entity, IOrdered
{
    private PersonSkill() { }

    internal PersonSkill(Guid personId, string name, int orderIndex)
    {
        PersonId = personId;
        Name = name.Trim();
        OrderIndex = orderIndex;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid PersonId { get; private set; }
    public Person? Person { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public int OrderIndex { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void SetOrder(int orderIndex) => OrderIndex = orderIndex;
}

/// <summary>
/// Something a person did that is worth knowing about — a certification, a release they
/// led, a course they finished.
///
/// The date is optional because half of what people list did not happen on a day anybody
/// wrote down, and demanding one would mean inventing them.
/// </summary>
public class PersonAchievement : Entity, IOrdered
{
    private PersonAchievement() { }

    internal PersonAchievement(Guid personId, string title, string? detail, DateOnly? achievedOn,
        int orderIndex)
    {
        var trimmed = (title ?? string.Empty).Trim();

        if (trimmed.Length == 0)
        {
            throw new DomainException("An achievement needs a title.");
        }

        if (trimmed.Length > 200)
        {
            throw new DomainException("An achievement title cannot be longer than 200 characters.");
        }

        PersonId = personId;
        Title = trimmed;
        Detail = string.IsNullOrWhiteSpace(detail) ? null : detail.Trim();
        AchievedOn = achievedOn;
        OrderIndex = orderIndex;
        CreatedAt = DateTimeOffset.UtcNow;
    }

    public Guid PersonId { get; private set; }
    public Person? Person { get; private set; }

    public string Title { get; private set; } = string.Empty;
    public string? Detail { get; private set; }
    public DateOnly? AchievedOn { get; private set; }
    public int OrderIndex { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public void SetOrder(int orderIndex) => OrderIndex = orderIndex;
}

/// <summary>
/// A profile picture, stored in the database.
///
/// In its own table, one row per person, for one reason: EF loads every scalar property of
/// an entity it reads, so a photo column on People would pull megabytes of image bytes into
/// memory every time anybody listed the roster, rendered a slide, or asked who is free this
/// week. Here it is loaded only when somebody asks for the picture itself.
///
/// In the database rather than on disk because this application is deployed as one process
/// against one SQL Server: a file path would mean a share to configure, back up and keep in
/// step with the database, for forty photographs.
/// </summary>
public class PersonPhoto : Entity
{
    /// <summary>
    /// Two megabytes is far more than a profile picture needs and still small enough that a
    /// row never becomes a problem to read.
    /// </summary>
    public const int MaxBytes = 2 * 1024 * 1024;

    public static readonly string[] AllowedContentTypes =
        ["image/jpeg", "image/png", "image/webp", "image/gif"];

    private PersonPhoto() { }

    internal PersonPhoto(Guid personId, byte[] bytes, string contentType)
    {
        if (bytes.Length > MaxBytes)
        {
            throw new DomainException(
                $"That picture is {bytes.Length / 1024 / 1024.0:0.#} MB. The limit is 2 MB.");
        }

        var normalised = (contentType ?? string.Empty).Trim().ToLowerInvariant();

        if (!AllowedContentTypes.Contains(normalised))
        {
            throw new DomainException(
                "That file is not an image the browser can show. Use JPEG, PNG, WebP or GIF.");
        }

        PersonId = personId;
        Bytes = bytes;
        ContentType = normalised;
        ByteCount = bytes.Length;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public Guid PersonId { get; private set; }
    public Person? Person { get; private set; }

    public byte[] Bytes { get; private set; } = [];
    public string ContentType { get; private set; } = "image/jpeg";
    public int ByteCount { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Changes whenever the picture does, so a browser can cache the image hard and still
    /// pick up a new one immediately — the URL carries this.
    /// </summary>
    public string Version => UpdatedAt.ToUnixTimeSeconds().ToString();
}
