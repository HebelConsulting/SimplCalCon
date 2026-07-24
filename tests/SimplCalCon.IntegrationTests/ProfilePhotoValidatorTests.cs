using SimplCalCon.Api.Http;

namespace SimplCalCon.IntegrationTests;

public sealed class ProfilePhotoValidatorTests
{
    [Fact]
    public void Accepts_a_small_png_with_sane_dimensions() =>
        Assert.True(ProfilePhotoValidator.IsValid(Png(256, 256)));

    [Fact]
    public void Rejects_oversized_dimensions() =>
        Assert.False(ProfilePhotoValidator.IsValid(Png(2000, 256)));

    [Fact]
    public void Rejects_zero_dimensions() =>
        Assert.False(ProfilePhotoValidator.IsValid(Png(0, 256)));

    [Fact]
    public void Rejects_a_non_png_signature() =>
        Assert.False(ProfilePhotoValidator.IsValid(new byte[24]));

    [Fact]
    public void Rejects_too_short_input() =>
        Assert.False(ProfilePhotoValidator.IsValid([0x89, 0x50, 0x4E, 0x47]));

    [Fact]
    public void Rejects_input_over_one_megabyte()
    {
        var big = new byte[(1024 * 1024) + 1];
        Png(256, 256).CopyTo(big, 0);
        Assert.False(ProfilePhotoValidator.IsValid(big));
    }

    private static byte[] Png(uint width, uint height)
    {
        var bytes = new byte[24];
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        signature.CopyTo(bytes);
        bytes[11] = 13;
        bytes[12] = (byte)'I';
        bytes[13] = (byte)'H';
        bytes[14] = (byte)'D';
        bytes[15] = (byte)'R';
        WriteBigEndian(bytes, 16, width);
        WriteBigEndian(bytes, 20, height);
        return bytes;
    }

    private static void WriteBigEndian(byte[] bytes, int offset, uint value)
    {
        bytes[offset] = (byte)(value >> 24);
        bytes[offset + 1] = (byte)(value >> 16);
        bytes[offset + 2] = (byte)(value >> 8);
        bytes[offset + 3] = (byte)value;
    }
}
