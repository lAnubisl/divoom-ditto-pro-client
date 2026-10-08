using Divoom;

namespace Divoom.Api.Endpoints;

internal static class AnimationEndpoints
{
    public static void Map(RouteGroupBuilder api) => api.MapPost("/animations", SendAsync);

    private static async Task<IResult> SendAsync(HttpRequest request, IDittoProClient client, IHostApplicationLifetime lifetime)
    {
        using var timeout = DeviceOperationTimeout.Create(request, lifetime);
        using var stream = await MediaUpload.ReadAsync(request, gif: true, timeout.Token);
        var frames = MediaUpload.Decode(() => DittoMedia.LoadGif(stream));
        _ = DittoMediaPreparation.AnimationFileSize(frames); // Validate the upload bound before accessing Bluetooth.
        await client.SendAnimationAsync(frames, cancellationToken: timeout.Token);
        return Results.Ok(new { status = "sent", frames = frames.Count });
    }
}
