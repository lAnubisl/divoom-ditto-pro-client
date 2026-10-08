using Divoom;

namespace Divoom.Api.Endpoints;

internal static class ImageEndpoints
{
    public static void Map(RouteGroupBuilder api) => api.MapPost("/images", SendAsync);

    private static async Task<IResult> SendAsync(HttpRequest request, IDittoProClient client, IHostApplicationLifetime lifetime)
    {
        using var timeout = DeviceOperationTimeout.Create(request, lifetime);
        using var stream = await MediaUpload.ReadAsync(request, gif: false, timeout.Token);
        var image = MediaUpload.Decode(() => DittoMedia.LoadImage(stream));
        await client.SendImageAsync(image, timeout.Token);
        return Results.Ok(new { status = "sent", width = DittoImage.Width, height = DittoImage.Height });
    }
}
