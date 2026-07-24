namespace SimplCalCon.Api.Http;

/// <summary>
/// A cheap byte-level guard for uploaded profile photos (ADR 0035) — deliberately NOT a
/// decoder. Clients always send a normalized 256×256 PNG, so the server only confirms the
/// bytes are a small PNG with sane <c>IHDR</c> dimensions. This removes an entire class of
/// server-side image-decoding vulnerabilities and dependencies.
/// </summary>
public static class ProfilePhotoValidator
{
    private const int MaxBytes = 1024 * 1024;
    private const int MaxDimension = 1024;

    private static ReadOnlySpan<byte> PngSignature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    public static bool IsValid(byte[] bytes)
    {
        // Need at least the 8-byte signature + IHDR length/type + width/height (through byte 23).
        if (bytes.Length is 0 or > MaxBytes || bytes.Length < 24)
        {
            return false;
        }

        if (!bytes.AsSpan(0, 8).SequenceEqual(PngSignature))
        {
            return false;
        }

        // The first chunk of a PNG must be IHDR (type at bytes 12..15).
        if (bytes[12] != 'I' || bytes[13] != 'H' || bytes[14] != 'D' || bytes[15] != 'R')
        {
            return false;
        }

        var width = ReadBigEndian(bytes, 16);
        var height = ReadBigEndian(bytes, 20);
        return width is >= 1 and <= MaxDimension && height is >= 1 and <= MaxDimension;
    }

    private static uint ReadBigEndian(byte[] bytes, int offset) =>
        ((uint)bytes[offset] << 24) | ((uint)bytes[offset + 1] << 16) | ((uint)bytes[offset + 2] << 8) | bytes[offset + 3];
}
