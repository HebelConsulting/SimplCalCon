using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Api.Hypermedia;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Application.Abstractions.Scheduling;

namespace SimplCalCon.Api.Controllers;

/// <summary>The signed-in user's pending calendar invitations — the web/REST view of the schedule-inbox (ADR 0045).</summary>
[Route("api/invitations")]
public sealed class InvitationsController(IInvitationService invitations, IAclService acl) : ApiControllerBase(acl)
{
    [HttpGet]
    [HttpHead]
    public async Task<ActionResult<CollectionResource<InvitationResource>>> List(CancellationToken cancellationToken)
    {
        var items = await invitations.ListAsync(CurrentUserId, cancellationToken);
        return new CollectionResource<InvitationResource>
        {
            Items = items.Select(ResourceMapper.MapInvitation).ToList(),
            Links = { new Link("self", "/api/invitations") },
        };
    }

    [HttpGet("count")]
    [HttpHead("count")]
    public async Task<ActionResult<InvitationCountResource>> Count(CancellationToken cancellationToken) =>
        new InvitationCountResource(await invitations.CountAsync(CurrentUserId, cancellationToken));

    // Accept/tentative/decline. A state transition on the invitation, so a POST verb sub-resource (ADR 0009).
    [HttpPost("respond")]
    public async Task<IActionResult> Respond(
        [FromBody] InvitationRespondRequest request, CancellationToken cancellationToken)
    {
        var response = request.Response?.ToLowerInvariant() switch
        {
            "accepted" => InvitationResponse.Accepted,
            "tentative" => InvitationResponse.Tentative,
            "declined" => InvitationResponse.Declined,
            _ => (InvitationResponse?)null,
        };

        if (response is not { } chosen || string.IsNullOrWhiteSpace(request.ResourceName))
        {
            return BadRequest("A resourceName and a response of accepted|tentative|declined are required.");
        }

        return await invitations.RespondAsync(CurrentUserId, request.ResourceName, chosen, cancellationToken)
            ? NoContent()
            : NotFound();
    }
}
