namespace Divoom.Api;

internal static class DeviceOperationTimeout
{
    public static CancellationTokenSource Create(HttpRequest request, IHostApplicationLifetime lifetime)
    {
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(request.HttpContext.RequestAborted, lifetime.ApplicationStopping);
        timeout.CancelAfter(TimeSpan.FromSeconds(180));
        return timeout;
    }
}
