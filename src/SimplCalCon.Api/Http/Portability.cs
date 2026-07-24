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

    public static async Task<byte[]> ReadBytesAsync(IFormFile file, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    /// <summary>True when the upload is a zip archive (by name, content type, or the PK magic bytes).</summary>
    public static bool IsZip(IFormFile file, byte[] bytes) =>
        (file.FileName?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ?? false)
        || string.Equals(file.ContentType, "application/zip", StringComparison.OrdinalIgnoreCase)
        || (bytes.Length >= 4 && bytes[0] == 0x50 && bytes[1] == 0x4B && bytes[2] == 0x03 && bytes[3] == 0x04);

    /// <summary>Decodes uploaded bytes as UTF-8 text, honouring a byte-order mark if present.</summary>
    public static string Decode(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Imports an uploaded file: a zip archive fans out to every matching entry (a Google export),
    /// otherwise the file is treated as a single .ics/.vcf document. Throws
    /// <see cref="System.IO.InvalidDataException"/> for a corrupt archive (the caller maps it to 400).
    /// </summary>
    public static Task<ImportOutcome> RunImportAsync(
        IObjectImportExport importExport, Guid collectionId, IFormFile file, byte[] bytes,
        string? onConflict, Guid? authorPrincipalId, CancellationToken cancellationToken) =>
        IsZip(file, bytes)
            ? importExport.ImportArchiveAsync(collectionId, bytes, Conflict(onConflict), authorPrincipalId, cancellationToken)
            : importExport.ImportAsync(collectionId, Decode(bytes), Conflict(onConflict), authorPrincipalId, cancellationToken);

    public static ImportResultResource Map(ImportOutcome outcome) => new()
    {
        Imported = outcome.Imported,
        Skipped = outcome.Skipped,
        Failed = outcome.Failed,
        Errors = outcome.Errors,
    };

    public static ImportResultResource Map(ArchiveImportOutcome result) => new()
    {
        Imported = result.Import.Imported,
        Skipped = result.Import.Skipped,
        Failed = result.Import.Failed,
        CreatedCollections = result.CreatedCollections,
        Errors = result.Import.Errors,
    };

    public static FileContentResult Download(string document, string contentType, string fileName) =>
        Download(Encoding.UTF8.GetBytes(document), contentType, fileName);

    public static FileContentResult Download(byte[] bytes, string contentType, string fileName) =>
        new(bytes, contentType) { FileDownloadName = fileName };
}
