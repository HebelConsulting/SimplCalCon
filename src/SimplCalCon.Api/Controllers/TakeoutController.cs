using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Errors.Exceptions.Authorization;
using SimplCalCon.Api.Http;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Storage;

namespace SimplCalCon.Api.Controllers;

/// <summary>
/// Account-wide data takeout for server-to-server migration (ADR 0013/0029): download a
/// self-describing ZIP of all your owned collections, or upload one to recreate them here.
/// </summary>
[Route("api/takeout")]
public sealed class TakeoutController(IAccountTakeout takeout, IAclService acl) : ApiControllerBase(acl)
{
    [HttpGet]
    public async Task<IActionResult> Export(CancellationToken cancellationToken)
    {
        var archive = await takeout.ExportAsync(CurrentUserId, cancellationToken);
        return Portability.Download(archive, "application/zip", "takeout.zip");
    }

    [HttpHead]
    public IActionResult HeadExport() => Ok();

    [HttpPost]
    public async Task<ActionResult<TakeoutImportResource>> Import(
        IFormFile? file, [FromForm] string? onConflict, CancellationToken cancellationToken)
    {
        if (CurrentTenantId is not { } tenantId)
        {
            throw new InsufficientRightsException();
        }

        if (file is null or { Length: 0 })
        {
            return BadRequest("A takeout .zip is required.");
        }

        var archive = await Portability.ReadBytesAsync(file, cancellationToken);
        var result = await takeout.ImportAsync(
            CurrentUserId, tenantId, archive, Portability.Conflict(onConflict), cancellationToken);

        return new TakeoutImportResource
        {
            CollectionsCreated = result.CollectionsCreated,
            Imported = result.Imported,
            Skipped = result.Skipped,
            Failed = result.Failed,
            Errors = result.Errors,
        };
    }
}
