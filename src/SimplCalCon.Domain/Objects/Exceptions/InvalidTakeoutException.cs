namespace SimplCalCon.Domain.Objects.Exceptions;

/// <summary>An uploaded takeout archive is missing or has an unreadable manifest (ADR 0029).</summary>
public sealed class InvalidTakeoutException(string detail)
    : Exception($"The takeout archive is invalid: {detail}");
