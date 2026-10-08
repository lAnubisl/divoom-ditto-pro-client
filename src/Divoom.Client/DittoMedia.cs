using StbImageSharp;

namespace Divoom;

/// <summary>Managed, portable image/GIF decoding. Does not use Windows imaging or native libraries.</summary>
public static class DittoMedia
{
    public static DittoImage LoadImage(string path)
    {
        using var stream = File.OpenRead(path);
        return LoadImage(stream);
    }

    public static DittoImage LoadImage(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return Resize(ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha));
    }

    public static IReadOnlyList<DittoAnimationFrame> LoadGif(string path)
    {
        using var stream = File.OpenRead(path);
        return LoadGif(stream);
    }

    public static IReadOnlyList<DittoAnimationFrame> LoadGif(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var frames = new List<DittoAnimationFrame>();
        // Decoder composites GIF transparency/disposal; copy each frame before advancing its enumerator.
        foreach (var frame in ImageResult.AnimatedGifFramesFromStream(stream, ColorComponents.RedGreenBlueAlpha))
        {
            if (frames.Count == 1024) throw new ArgumentException("GIF exceeds 1024 decoded frames.", nameof(stream));
            frames.Add(new(Resize(frame), TimeSpan.FromMilliseconds(frame.DelayInMs > 0 ? Math.Min(frame.DelayInMs, 65535) : 100)));
        }
        if (frames.Count == 0) throw new ArgumentException("GIF contains no frames.", nameof(stream));
        return frames;
    }

    private static DittoImage Resize(ImageResult source)
    {
        if (source.Width < 1 || source.Height < 1) throw new InvalidDataException("Invalid image dimensions.");
        var scale = Math.Min(16.0 / source.Width, 16.0 / source.Height);
        var width = Math.Clamp((int)Math.Round(source.Width * scale), 1, 16);
        var height = Math.Clamp((int)Math.Round(source.Height * scale), 1, 16);
        var left = (16 - width) / 2;
        var top = (16 - height) / 2;
        var rgb = new byte[768];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var sx = Math.Min(source.Width - 1, (int)((x + 0.5) * source.Width / width));
            var sy = Math.Min(source.Height - 1, (int)((y + 0.5) * source.Height / height));
            var input = (sy * source.Width + sx) * 4;
            var output = ((y + top) * 16 + x + left) * 3;
            for (var color = 0; color < 3; color++) rgb[output + color] = (byte)((source.Data[input + color] * source.Data[input + 3] + 127) / 255);
        }
        return new DittoImage(rgb);
    }
}
