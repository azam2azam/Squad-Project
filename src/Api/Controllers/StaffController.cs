using Application.Staff;
using Domain.Enums;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Staff scheduling: who is committed where, when they are away, and what work is on them.
///
/// Reads are open to anyone signed in — knowing who has capacity is not privileged. Writes
/// follow the thing being changed: work items take the board's edit rights, availability
/// takes the roster's admin gate, both enforced in the handlers.
/// </summary>
[ApiController]
[Route("api/v1/staff")]
[Produces("application/json")]
public sealed class StaffController(ISender sender) : ControllerBase
{
    /// <summary>The team schedule: people by week, with committed and free capacity.</summary>
    [HttpGet("schedule")]
    [ProducesResponseType<StaffScheduleDto>(StatusCodes.Status200OK)]
    public async Task<ActionResult<StaffScheduleDto>> Schedule(
        [FromQuery] DateOnly? from,
        [FromQuery] int weeks = 8,
        CancellationToken cancellationToken = default)
        => Ok(await sender.Send(new GetStaffScheduleQuery(from, weeks), cancellationToken));

    /// <summary>One person: commitments, availability, work items and recorded activity.</summary>
    [HttpGet("{personId:guid}")]
    [ProducesResponseType<PersonProfileDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PersonProfileDto>> Profile(
        Guid personId, CancellationToken cancellationToken)
        => Ok(await sender.Send(new GetPersonProfileQuery(personId), cancellationToken));

    /// <summary>The vocabularies the pickers need, so the client hardcodes neither.</summary>
    [HttpGet("options")]
    public ActionResult<object> Options() => Ok(new
    {
        availabilityKinds = StaffMetadata.AvailabilityOrder.Select(k => new
        {
            value = (int)k,
            name = k.ToString(),
            label = StaffMetadata.Label(k),
            color = StaffMetadata.Color(k)
        }),
        workItemStatuses = StaffMetadata.WorkItemOrder.Select(s => new
        {
            value = (int)s,
            name = s.ToString(),
            label = StaffMetadata.Label(s),
            color = StaffMetadata.Color(s)
        })
    });

    [HttpPut("assignments/{memberId:guid}")]
    [ProducesResponseType<AssignmentDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AssignmentDto>> ScheduleAssignment(
        Guid memberId, [FromBody] ScheduleAssignmentRequest request,
        CancellationToken cancellationToken)
        => Ok(await sender.Send(
            new ScheduleAssignmentCommand(memberId, request.AllocationPercent,
                request.StartsOn, request.EndsOn),
            cancellationToken));

    // ---- Availability ----

    [HttpPost("availability")]
    [ProducesResponseType<AvailabilityDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AvailabilityDto>> SaveAvailability(
        [FromBody] SaveAvailabilityRequest request, CancellationToken cancellationToken)
        => Ok(await sender.Send(
            new SaveAvailabilityCommand(request.Id, request.PersonId, request.FromDate,
                request.ToDate, request.Kind, request.CapacityPercent, request.Note),
            cancellationToken));

    [HttpDelete("availability/{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteAvailability(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteAvailabilityCommand(id), cancellationToken);
        return NoContent();
    }
}

/// <summary>Work items: tasks on a board, optionally assigned to somebody.</summary>
[ApiController]
[Route("api/v1/work-items")]
[Produces("application/json")]
public sealed class WorkItemsController(ISender sender) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<WorkItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WorkItemDto>>> List(
        [FromQuery] Guid? boardId,
        [FromQuery] Guid? personId,
        [FromQuery] bool includeDone = true,
        CancellationToken cancellationToken = default)
        => Ok(await sender.Send(
            new ListWorkItemsQuery(boardId, personId, includeDone), cancellationToken));

    [HttpPost]
    [ProducesResponseType<WorkItemDto>(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<WorkItemDto>> Create(
        [FromBody] SaveWorkItemRequest request, CancellationToken cancellationToken)
    {
        var created = await sender.Send(
            new CreateWorkItemCommand(request.BoardId, request.Title ?? string.Empty,
                request.PersonId, request.Status, request.PlannedStart, request.PlannedEnd,
                request.EffortHours, request.Detail),
            cancellationToken);

        return CreatedAtAction(nameof(List), new { }, created);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<WorkItemDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<WorkItemDto>> Update(
        Guid id, [FromBody] SaveWorkItemRequest request, CancellationToken cancellationToken)
        => Ok(await sender.Send(
            new UpdateWorkItemCommand(id, request.Title ?? string.Empty, request.PersonId,
                request.Status, request.PlannedStart, request.PlannedEnd,
                request.EffortHours, request.Detail),
            cancellationToken));

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        await sender.Send(new DeleteWorkItemCommand(id), cancellationToken);
        return NoContent();
    }
}

public sealed record ScheduleAssignmentRequest(
    int? AllocationPercent, DateOnly? StartsOn, DateOnly? EndsOn);

public sealed record SaveAvailabilityRequest(
    Guid? Id, Guid PersonId, DateOnly FromDate, DateOnly ToDate,
    AvailabilityKind Kind, int CapacityPercent, string? Note);

public sealed record SaveWorkItemRequest(
    Guid BoardId, string? Title, Guid? PersonId, WorkItemStatus Status,
    DateOnly? PlannedStart, DateOnly? PlannedEnd, double? EffortHours, string? Detail);
