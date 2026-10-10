using Divoom;

namespace Divoom.Api;

internal sealed class AudioPlaybackLifetime(IDivoomAudioPlayer player) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => player.StopAsync(cancellationToken);
}
