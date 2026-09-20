using Application.People.Profiles;
using Domain.Common;
using Domain.Entities;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// The team directory and the profile each person keeps about themselves.
///
/// Reading is open to anyone signed in — knowing who is on the team and what they work with
/// is not privileged, and a directory only half the company can open is not a directory.
/// Writing is the person's own or an administrator's, enforced in the handlers.
/// </summary>
[ApiController]
[Route("api/v1/profiles")]
[Produces("application/json")]
[Authorize]
public sealed class ProfilesController(ISender sender) : ControllerBase
{
    /// <summary>Everyone, as cards. Searches names, headlines and skills.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<DirectoryEntryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DirectoryEntryDto>>> Directory(
        [FromQuery] string? q,
        [FromQuery] bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        Ok(await sender.Send(new GetTeamDirectoryQuery(q, includeInactive), cancellationToken));

    [HttpGet("{personId:guid}")]
    [ProducesResponseType<PersonProfileCardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PersonProfileCardDto>> Get(
        Guid personId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new GetPersonProfileCardQuery(personId), cancellationToken));

    [HttpPut("{personId:guid}")]
    [ProducesResponseType<PersonProfileCardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PersonProfileCardDto>> Update(
        Guid personId, [FromBody] UpdateProfileRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new UpdatePersonProfileCommand(personId, request.Headline, request.About),
            cancellationToken));

    [HttpPost("{personId:guid}/skills")]
    [ProducesResponseType<PersonProfileCardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PersonProfileCardDto>> AddSkills(
        Guid personId, [FromBody] AddSkillsRequest request, CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new AddPersonSkillsCommand(personId, request.Names ?? string.Empty), cancellationToken));

    [HttpDelete("{personId:guid}/skills/{skillId:guid}")]
    [ProducesResponseType<PersonProfileCardDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PersonProfileCardDto>> RemoveSkill(
        Guid personId, Guid skillId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new RemovePersonSkillCommand(personId, skillId), cancellationToken));

    [HttpPost("{personId:guid}/achievements")]
    [ProducesResponseType<PersonProfileCardDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PersonProfileCardDto>> AddAchievement(
        Guid personId, [FromBody] AddAchievementRequest request,
        CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new AddPersonAchievementCommand(personId, request.Title ?? string.Empty,
                request.Detail, request.AchievedOn),
            cancellationToken));

    [HttpDelete("{personId:guid}/achievements/{achievementId:guid}")]
    [ProducesResponseType<PersonProfileCardDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PersonProfileCardDto>> RemoveAchievement(
        Guid personId, Guid achievementId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(
            new RemovePersonAchievementCommand(personId, achievementId), cancellationToken));

    /// <summary>
    /// The picture itself. Anonymous and cached hard: an &lt;img&gt; tag cannot send a bearer
    /// token, and the URL carries a version that changes whenever the photo does, so a long
    /// cache is safe and a new photo still appears immediately.
    /// </summary>
    [HttpGet("{personId:guid}/photo")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Photo(Guid personId, CancellationToken cancellationToken)
    {
        var photo = await sender.Send(new GetPersonPhotoQuery(personId), cancellationToken);
        if (photo is null) return NotFound();

        Response.Headers.CacheControl = "public, max-age=31536000, immutable";

        return File(photo.Bytes, photo.ContentType, lastModified: null,
            entityTag: new Microsoft.Net.Http.Headers.EntityTagHeaderValue($"\"{photo.Version}\""));
    }

    /// <summary>
    /// Replaces the picture. Multipart because this is a file from a file picker; the size
    /// limit is enforced here as well as in the domain so an oversized upload is refused
    /// before it is read into memory.
    /// </summary>
    [HttpPost("{personId:guid}/photo")]
    [RequestSizeLimit(PersonPhoto.MaxBytes + 8192)]
    [ProducesResponseType<PersonProfileCardDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PersonProfileCardDto>> UploadPhoto(
        Guid personId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            throw new DomainException("Choose a picture to upload.");
        }

        if (file.Length > PersonPhoto.MaxBytes)
        {
            throw new DomainException(
                $"That picture is {file.Length / 1024 / 1024.0:0.#} MB. The limit is 2 MB.");
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);

        return Ok(await sender.Send(
            new SetPersonPhotoCommand(personId, buffer.ToArray(), file.ContentType),
            cancellationToken));
    }

    [HttpDelete("{personId:guid}/photo")]
    [ProducesResponseType<PersonProfileCardDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PersonProfileCardDto>> RemovePhoto(
        Guid personId, CancellationToken cancellationToken) =>
        Ok(await sender.Send(new SetPersonPhotoCommand(personId, null, null), cancellationToken));
}

public sealed record UpdateProfileRequest(string? Headline, string? About);

public sealed record AddSkillsRequest(string? Names);

public sealed record AddAchievementRequest(string? Title, string? Detail, DateOnly? AchievedOn);
