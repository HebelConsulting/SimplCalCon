using System.Text;
using Microsoft.AspNetCore.Mvc;
using SimplCalCon.Api.Contracts;
using SimplCalCon.Application.Abstractions.Storage;

namespace SimplCalCon.Api.Http;

/// <summary>Shared import/export plumbing for the portability endpoints (ADR 0013/0029).</summary>
internal static class Portability
{
    public static ImportConflictMode Conflict(string? onConflict) =>
        string.Equals(onConflict, "replace", StringComparison.OrdinalIgnoreCase)
            ? ImportConflictMode.Replace
            : ImportConflictMode.Skip;

    public static async Task<string> ReadAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8);
        return await reader.ReadToEndAsync(cancellationToken);
    }

    public static async Task<byte[]> ReadBytesAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    public static ImportResultResource Map(ImportOutcome outcome) => new()
    {
        Imported = outcome.Imported,
        Skipped = outcome.Skipped,
        Failed = outcome.Failed,
        Errors = outcome.Errors,
    };

    public static FileContentResult Download(string document, string contentType, string fileName) =>
        Download(Encoding.UTF8.GetBytes(document), contentType, fileName);

    public static FileContentResult Download(byte[] bytes, string contentType, string fileName) =>
        new(bytes, contentType) { FileDownloadName = fileName };
}
