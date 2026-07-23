using Microsoft.AspNetCore.Http;

namespace SimplCalCon.Api.Errors.Exceptions.AppPasswords;

public sealed class AppPasswordNotFoundException(Guid id)
    : AppPasswordException(
        "APP_PASSWORD_NOT_FOUND",
        StatusCodes.Status404NotFound,
        $"App password '{id}' was not found.");
