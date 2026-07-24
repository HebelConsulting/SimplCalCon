using Microsoft.AspNetCore.Http;

namespace SimplCalCon.Api.Errors.Exceptions.Contacts;

/// <summary>The edited vCard's UID collides with another contact in the same book (ADR 0036).</summary>
public sealed class VCardUidConflictException()
    : ContactException(
        "VCARD_UID_CONFLICT",
        StatusCodes.Status409Conflict,
        "Another contact in this address book already uses this UID.");
