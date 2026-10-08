namespace Divoom;

/// <summary>Offline preparation using the exact display codec. Does not open a transport.</summary>
public static class DittoMediaPreparation
{
    public static DittoImage PreviewImage(DittoImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return new(DivoomProtocol.PreviewPixels(DivoomProtocol.EncodeImage(image.Rgb.ToArray())));
    }

    public static int ImagePacketSize(DittoImage image)
    {
        ArgumentNullException.ThrowIfNull(image);
        return DivoomProtocol.EncodeImage(image.Rgb.ToArray()).Length + 13;
    }

    public static int AnimationFileSize(IReadOnlyList<DittoAnimationFrame> frames)
    {
        ArgumentNullException.ThrowIfNull(frames);
        return DivoomProtocol.AnimationStream(frames.ToArray()).Length;
    }
}
