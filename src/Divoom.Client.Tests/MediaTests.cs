using Divoom;
using NUnit.Framework;

public sealed class MediaTests
{
    [Test]
    public void PngDecodesToRgbPixels()
    {
        var png = DittoMedia.LoadImage(Fixture("demo.png"));
        Assert.That(png.Rgb.Length, Is.EqualTo(768));
        Assert.That(png.Rgb.Span[..3].ToArray(), Is.EqualTo(new byte[] { 0, 160, 255 }));
    }

    [Test]
    public void GifPreservesFrameDelaysAndIndependentResizedPixels()
    {
        var gif = DittoMedia.LoadGif(Fixture("two-frame.gif"));
        Assert.That(gif, Has.Count.EqualTo(2));
        Assert.That(gif[0].Duration.TotalMilliseconds, Is.EqualTo(200));
        Assert.That(gif[1].Duration.TotalMilliseconds, Is.EqualTo(700));
        Assert.That(gif[0].Image.Rgb.Span[0], Is.EqualTo(255));
        Assert.That(gif[0].Image.Rgb.Span[2], Is.EqualTo(0));
        Assert.That(gif[1].Image.Rgb.Span[0], Is.EqualTo(0));
        Assert.That(gif[1].Image.Rgb.Span[2], Is.EqualTo(255));
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);
}
