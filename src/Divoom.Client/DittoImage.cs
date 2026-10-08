namespace Divoom;

/// <summary>Immutable 16x16 RGB888 image, row-major, top-left first.</summary>
public sealed class DittoImage
{
    private readonly byte[] pixels;
    public const int Width = 16;
    public const int Height = 16;
    public ReadOnlyMemory<byte> Rgb => pixels;

    public DittoImage(ReadOnlySpan<byte> rgb)
    {
        if (rgb.Length != Width * Height * 3) throw new ArgumentException("Expected 768 RGB888 bytes (16x16).", nameof(rgb));
        pixels = rgb.ToArray();
    }
}
