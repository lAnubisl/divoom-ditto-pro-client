namespace Divoom;

public sealed class DittoAnimationFrame
{
    public DittoImage Image { get; }
    public TimeSpan Duration { get; }

    public DittoAnimationFrame(DittoImage image, TimeSpan duration)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (duration.TotalMilliseconds is < 1 or > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be 1..65535 ms.");
        Image = image;
        Duration = duration;
    }
}
