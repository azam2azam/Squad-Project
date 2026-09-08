using Application.Abstractions;
using Application.Boards.Queries;
using Application.Integrations;
using MediatR;
using Microsoft.AspNetCore.Mvc;

namespace Api.Controllers;

/// <summary>
/// Administering the Jira connection (spec section 10).
///
/// Admin-only throughout: these endpoints read and write credentials that act on behalf
/// of the whole organisation. The API token is write-only — it goes in, and only a masked
/// hint ever comes back.
/// </summary>
[ApiController]
[Route("api/v1/integrations/jira")]
[Produces("application/json")]
public sealed class IntegrationsController(
    IJiraSettingsService settingsService,
    IBoardAuthorizer authorizer,
    ICurrentUser currentUser,
    ISender sender) : ControllerBase
{
    /// <summary>The current connection, with the token masked.</summary>
    [HttpGet]
    [ProducesResponseType<JiraSettingsView>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<JiraSettingsView>> Get(CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();
        return Ok(await settingsService.GetAsync(cancellationToken));
    }

    /// <summary>
    /// Saves the connection. Leave <c>apiToken</c> empty to keep the stored one —
    /// the client is never given the token, so it cannot send it back.
    /// </summary>
    [HttpPut]
    [ProducesResponseType<JiraSettingsView>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<JiraSettingsView>> Save(
        [FromBody] SaveJiraSettingsRequest request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        var saved = await settingsService.SaveAsync(
            new SaveJiraSettings(
                request.BaseUrl ?? string.Empty,
                request.Email ?? string.Empty,
                request.ApiToken,
                request.Enabled,
                request.AutoApply,
                request.SyncIntervalMinutes),
            cancellationToken);

        return Ok(saved);
    }

    /// <summary>Forgets the connection entirely, including the stored token.</summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();
        await settingsService.ClearAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Makes a real call to Jira, so "not configured" can be told apart from
    /// "configured but the token is wrong".
    /// </summary>
    [HttpPost("test")]
    [ProducesResponseType<JiraConnectionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<JiraConnectionDto>> Test(
        [FromBody] TestJiraRequest? request, CancellationToken cancellationToken)
        => Ok(await sender.Send(new GetJiraConnectionQuery(request?.ProjectKey), cancellationToken));

    /// <summary>
    /// Runs the sync now, across every board with a Jira project key.
    ///
    /// Pressing this is an explicit instruction, so it writes even when auto-apply is off —
    /// unlike the scheduled run, which respects that switch. The admin's name goes into
    /// each board's audit trail, so the change is attributable to a person rather than to
    /// "system".
    /// </summary>
    [HttpPost("sync")]
    [ProducesResponseType<JiraSyncReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<JiraSyncReport>> SyncNow(CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        return Ok(await sender.Send(
            new SyncBoardsFromJiraCommand(currentUser.DisplayName, RespectAutoApply: false),
            cancellationToken));
    }
}

public sealed record SaveJiraSettingsRequest(
    string? BaseUrl,
    string? Email,
    string? ApiToken,
    bool Enabled,
    bool AutoApply,
    int SyncIntervalMinutes);

public sealed record TestJiraRequest(string? ProjectKey);

/// <summary>
/// Administering the Smartsheet connection.
///
/// A separate controller from Jira rather than a shared one: the two providers take
/// different credentials — Smartsheet a bearer token, Jira an email and API token — and a
/// merged shape would carry fields that are meaningless for one of them.
///
/// Admin-only throughout, and the access token is write-only: it goes in, and only a
/// masked hint ever comes back.
/// </summary>
[ApiController]
[Route("api/v1/integrations/smartsheet")]
[Produces("application/json")]
public sealed class SmartsheetIntegrationController(
    ISmartsheetSettingsService settingsService,
    IBoardAuthorizer authorizer,
    ICurrentUser currentUser,
    ISender sender) : ControllerBase
{
    /// <summary>The current connection, with the token masked.</summary>
    [HttpGet]
    [ProducesResponseType<SmartsheetSettingsView>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SmartsheetSettingsView>> Get(CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();
        return Ok(await settingsService.GetAsync(cancellationToken));
    }

    /// <summary>
    /// Saves the connection. Leave <c>accessToken</c> empty to keep the stored one — the
    /// client is never given the token, so it cannot send it back.
    /// </summary>
    [HttpPut]
    [ProducesResponseType<SmartsheetSettingsView>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SmartsheetSettingsView>> Save(
        [FromBody] SaveSmartsheetRequest request, CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        return Ok(await settingsService.SaveAsync(
            new SaveSmartsheetSettings(
                request.BaseUrl ?? string.Empty,
                request.AccessToken,
                request.Enabled,
                request.AutoApply,
                request.SyncIntervalMinutes,
                request.ProgressColumn,
                request.StatusColumn),
            cancellationToken));
    }

    /// <summary>Forgets the connection entirely, including the stored token.</summary>
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Clear(CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();
        await settingsService.ClearAsync(cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Makes a real call to Smartsheet, so "not configured" can be told apart from
    /// "configured but the token is wrong".
    /// </summary>
    [HttpPost("test")]
    [ProducesResponseType<SmartsheetConnectionDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SmartsheetConnectionDto>> Test(
        [FromBody] TestSmartsheetRequest? request, CancellationToken cancellationToken)
        => Ok(await sender.Send(
            new GetSmartsheetConnectionQuery(request?.SheetId), cancellationToken));

    /// <summary>
    /// Runs the sync now, across every board with a sheet id. Pressing this is an
    /// explicit instruction, so it writes even when auto-apply is off.
    /// </summary>
    [HttpPost("sync")]
    [ProducesResponseType<SmartsheetSyncReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SmartsheetSyncReport>> SyncNow(CancellationToken cancellationToken)
    {
        authorizer.EnsureIsAdmin();

        return Ok(await sender.Send(
            new SyncBoardsFromSmartsheetCommand(currentUser.DisplayName, RespectAutoApply: false),
            cancellationToken));
    }
}

public sealed record SaveSmartsheetRequest(
    string? BaseUrl,
    string? AccessToken,
    bool Enabled,
    bool AutoApply,
    int SyncIntervalMinutes,
    string? ProgressColumn,
    string? StatusColumn);

public sealed record TestSmartsheetRequest(string? SheetId);
