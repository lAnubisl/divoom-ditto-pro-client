using StbImageSharp;

namespace Divoom.Api;

internal static class MediaUpload
{
    public const int MaxBytes = 2 * 1024 * 1024;
    private const long MaxPixels = 1024 * 1024;

    public static async Task<MemoryStream> ReadAsync(HttpRequest request, bool gif, CancellationToken token)
    {
        var mediaType = request.ContentType?.Split(';', 2)[0].Trim().ToLowerInvariant();
        var accepted = gif ? new[] { "image/gif", "application/octet-stream" }
            : new[] { "image/png", "image/jpeg", "image/bmp", "image/gif", "application/octet-stream" };
        if (mediaType is null || !accepted.Contains(mediaType)) throw new BadHttpRequestException("Unsupported media type.", StatusCodes.Status415UnsupportedMediaType);
        if (request.ContentLength > MaxBytes) throw new BadHttpRequestException("Upload exceeds 2 MiB.", StatusCodes.Status413PayloadTooLarge);
        var stream = new MemoryStream();
        try
        {
            var buffer = new byte[16 * 1024];
            int count;
            while ((count = await request.Body.ReadAsync(buffer, token)) > 0)
            {
                if (stream.Length + count > MaxBytes) throw new BadHttpRequestException("Upload exceeds 2 MiB.", StatusCodes.Status413PayloadTooLarge);
                stream.Write(buffer, 0, count);
            }
            if (stream.Length == 0) throw new InvalidDataException("Upload is empty.");
            if (gif && (stream.Length < 6 || !(stream.GetBuffer().AsSpan(0, 6).SequenceEqual("GIF87a"u8) || stream.GetBuffer().AsSpan(0, 6).SequenceEqual("GIF89a"u8))))
                throw new InvalidDataException("Expected a GIF file.");
            stream.Position = 0;
            ImageInfo? info;
            try { info = ImageInfo.FromStream(stream); }
            catch (Exception error) when (error is not OperationCanceledException) { throw new InvalidDataException("Invalid media file.", error); }
            if (info is null || info.Value.Width <= 0 || info.Value.Height <= 0 || (long)info.Value.Width * info.Value.Height > MaxPixels)
                throw new InvalidDataException("Media must have at most 1,048,576 pixels per frame.");
            stream.Position = 0;
            return stream;
        }
        catch { stream.Dispose(); throw; }
    }

    public static T Decode<T>(Func<T> decode)
    {
        try { return decode(); }
        catch (Exception error) when (error is not OperationCanceledException) { throw new InvalidDataException("Invalid media file.", error); }
    }
}
