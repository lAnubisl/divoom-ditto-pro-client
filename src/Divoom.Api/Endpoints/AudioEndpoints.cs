using Divoom;

namespace Divoom.Api.Endpoints;

internal static class AudioEndpoints
{
    private const string Route = "/audio/playback";

    public static void Map(RouteGroupBuilder api)
    {
        api.MapGet(Route, (IDivoomAudioPlayer player) => Results.Ok(player.Status));
        api.MapPut(Route, StartAsync);
        api.MapPatch(Route, SetVolumeAsync);
        api.MapDelete(Route, StopAsync);
    }

    private static async Task<IResult> StartAsync(AudioPlaybackRequest request, AudioStreamPolicy policy,
        IDivoomAudioPlayer player, HttpRequest http, IHostApplicationLifetime lifetime)
    {
        var source = policy.Validate(request.Url);
        var volume = AudioStreamPolicy.ValidateVolume(request.Volume, 0.25);
        using var timeout = CreateCommandTimeout(http, lifetime);
        await player.StartAsync(source, volume, timeout.Token);
        return Results.Accepted("/api" + Route, player.Status);
    }

    private static async Task<IResult> SetVolumeAsync(AudioVolumeRequest request,
        IDivoomAudioPlayer player, HttpRequest http, IHostApplicationLifetime lifetime)
    {
        var volume = AudioStreamPolicy.ValidateVolume(request.Volume);
        using var timeout = CreateCommandTimeout(http, lifetime);
        await player.SetVolumeAsync(volume, timeout.Token);
        return Results.Ok(player.Status);
    }

    private static async Task<IResult> StopAsync(IDivoomAudioPlayer player, HttpRequest http, IHostApplicationLifetime lifetime)
    {
        using var timeout = CreateCommandTimeout(http, lifetime);
        await player.StopAsync(timeout.Token);
        return Results.Ok(player.Status);
    }

    private static CancellationTokenSource CreateCommandTimeout(HttpRequest request, IHostApplicationLifetime lifetime)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(request.HttpContext.RequestAborted, lifetime.ApplicationStopping);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        return timeout;
    }
}
