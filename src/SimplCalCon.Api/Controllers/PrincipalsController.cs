using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Validation.AspNetCore;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Http;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions;

namespace SimplCalCon.Api.Controllers;

/// <summary>Searches users and groups in the caller's tenant for the sharing grantee picker (ADR 0007).</summary>
[ApiController]
[Route("api/principals")]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
public sealed class PrincipalsController(IPrincipalDirectory directory) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CollectionResource<PrincipalResource>>> Search(
        [FromQuery] string? q, CancellationToken cancellationToken)
    {
        if (User.GetTenantId() is not { } tenantId)
        {
            return new CollectionResource<PrincipalResource> { Items = [], Links = { new Link("self", "/api/principals") } };
        }

        var results = await directory.SearchAsync(tenantId, q, cancellationToken);
        return new CollectionResource<PrincipalResource>
        {
            Items = results.Select(p => new PrincipalResource
            {
                Id = p.Id,
                Kind = p.Kind,
                DisplayName = p.DisplayName,
                Email = p.Email,
            }).ToList(),
            Links = { new Link("self", "/api/principals") },
        };
    }
}
