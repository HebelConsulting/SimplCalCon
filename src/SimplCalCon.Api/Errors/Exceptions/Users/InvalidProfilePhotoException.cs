using Microsoft.AspNetCore.Http;

namespace SimplCalCon.Api.Errors.Exceptions.Users;

/// <summary>The uploaded profile photo failed the byte guard (ADR 0035): not a small PNG with sane dimensions.</summary>
public sealed class InvalidProfilePhotoException()
    : UserException(
        "INVALID_PROFILE_PHOTO",
        StatusCodes.Status400BadRequest,
        "The profile photo must be a PNG image no larger than 1 MB and 1024×1024 pixels.");
