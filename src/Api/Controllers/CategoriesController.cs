using Application.Categories;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Programmes above the boards — VIDA 4, AI, and so on.
///
/// Reading is open to anyone signed in, because every picker and filter needs the list.
/// Writing is admin-only, enforced in the handlers: a category regroups the whole
/// portfolio.
/// </summary>
[ApiController]
[Route("api/v1/categories")]
[Produces("application/json")]
public sealed class CategoriesController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<CategoryDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CategoryDto>>> List(
        [FromQuery] bool includeInactive = false, CancellationToken cancellationToken = default)
        => Ok(await sender.Send(new ListCategoriesQuery(includeInactive), cancellationToken));

    [HttpPost]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CategoryDto>> Create(
        [FromBody] CreateCategoryRequest request, CancellationToken cancellationToken)
    {
        var created = await sender.Send(
            new CreateCategoryCommand(
                request.Name ?? string.Empty, request.Description, request.Color ?? string.Empty),
            cancellationToken);

        return CreatedAtAction(nameof(List), new { }, created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CategoryDto>> Update(
        Guid id, [FromBody] UpdateCategoryRequest request, CancellationToken cancellationToken)
        => Ok(await sender.Send(
            new UpdateCategoryCommand(id, request.Name ?? string.Empty, request.Description,
                request.Color ?? string.Empty, request.OrderIndex),
            cancellationToken));

    /// <summary>
    /// Retires or restores a category. Retiring is soft: boards already in it keep their
    /// grouping rather than tipping back into the uncategorised pile.
    /// </summary>
    [HttpPut("{id:guid}/active")]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<CategoryDto>> SetActive(
        Guid id, [FromBody] SetCategoryActiveRequest request, CancellationToken cancellationToken)
        => Ok(await sender.Send(new SetCategoryActiveCommand(id, request.IsActive), cancellationToken));

    /// <summary>Moves many boards at once. Pass a null categoryId to clear them.</summary>
    [HttpPost("assign")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<object>> Assign(
        [FromBody] AssignBoardsRequest request, CancellationToken cancellationToken)
    {
        var moved = await sender.Send(
            new AssignBoardsToCategoryCommand(request.BoardIds ?? [], request.CategoryId),
            cancellationToken);

        return Ok(new { moved });
    }
}

public sealed record CreateCategoryRequest(string? Name, string? Description, string? Color);

public sealed record UpdateCategoryRequest(
    string? Name, string? Description, string? Color, int OrderIndex);

public sealed record SetCategoryActiveRequest(bool IsActive);

public sealed record AssignBoardsRequest(IReadOnlyList<Guid>? BoardIds, Guid? CategoryId);
