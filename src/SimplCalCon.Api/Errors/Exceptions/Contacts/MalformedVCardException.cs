using Microsoft.AspNetCore.Http;

namespace SimplCalCon.Api.Errors.Exceptions.Contacts;

/// <summary>The edited vCard could not be parsed, so it was refused (ADR 0036). Nothing is persisted.</summary>
public sealed class MalformedVCardException(string? detail = null)
    : ContactException(
        "INVALID_VCARD",
        StatusCodes.Status400BadRequest,
        "The vCard is not valid and was not saved. Check the BEGIN:VCARD / END:VCARD / VERSION lines and the property syntax."
            + (string.IsNullOrWhiteSpace(detail) ? "" : $" ({detail})"));
