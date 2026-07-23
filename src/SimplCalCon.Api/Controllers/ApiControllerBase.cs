using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Validation.AspNetCore;
using SimplCalCon.Api.Errors.Exceptions.Authorization;
using SimplCalCon.Api.Errors.Exceptions.Concurrency;
using SimplCalCon.Api.Http;
using SimplCalCon.Application.Abstractions.Acl;
using SimplCalCon.Domain.Acl;

namespace SimplCalCon.Api.Controllers;

/// <summary>Base for bearer-authenticated REST resource controllers (ADR 0009), with ACL + ETag helpers.</summary>
[ApiController]
[Authorize(AuthenticationSchemes = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)]
public abstract class ApiControllerBase(IAclService acl) : ControllerBase
{
    protected IAclService Acl { get; } = acl;

    protected Guid CurrentUserId => User.GetUserId();

    protected Guid? CurrentTenantId => User.GetTenantId();

    /// <summary>Throws 403 unless the caller's effective rights on the collection include <paramref name="required"/>.</summary>
    protected async Task RequireRightsAsync(Guid collectionId, AclRight required, CancellationToken cancellationToken)
    {
        var rights = await Acl.GetEffectiveRightsAsync(CurrentUserId, collectionId, cancellationToken);
        if ((rights & required) != required)
        {
            throw new InsufficientRightsException();
        }
    }

    /// <summary>Throws 403 unless the caller may manage the collection's grants (owner, or the `share`/`admin` right, ADR 0007).</summary>
    protected async Task RequireCanShareAsync(Guid collectionId, CancellationToken cancellationToken)
    {
        var rights = await Acl.GetEffectiveRightsAsync(CurrentUserId, collectionId, cancellationToken);
        if ((rights & AclRight.Share) != AclRight.Share && (rights & AclRight.Admin) != AclRight.Admin)
        {
            throw new InsufficientRightsException();
        }
    }

    /// <summary>Throws 412 when a supplied If-Match token doesn't match the resource's current ETag.</summary>
    protected void EnsureIfMatch(Guid currentToken)
    {
        if (RequireIfMatchAttribute.ReadToken(HttpContext) is { } token && token != currentToken)
        {
            throw new EtagMismatchException();
        }
    }
}
