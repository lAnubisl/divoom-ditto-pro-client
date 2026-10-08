using Divoom;

namespace Divoom.Api.Endpoints;

internal static class DisplayEndpoints
{
    public static void Map(RouteGroupBuilder api) => api.MapPost("/display/clock", ShowClockAsync);

    private static async Task<IResult> ShowClockAsync(HttpRequest request, IDittoProClient client, IHostApplicationLifetime lifetime)
    {
        using var timeout = DeviceOperationTimeout.Create(request, lifetime);
        await client.ShowClockAsync(timeout.Token);
        return Results.Ok(new { status = "sent", mode = "clock" });
    }
}
