using Divoom;

namespace Divoom.Api.Endpoints;

internal static class TimeEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        api.MapPost("/time/current", SendCurrentAsync);
        api.MapPost("/time", SendAsync);
    }

    private static async Task<IResult> SendCurrentAsync(HttpRequest request, IDittoProClient client, IHostApplicationLifetime lifetime)
    {
        using var timeout = DeviceOperationTimeout.Create(request, lifetime);
        await client.SendCurrentDateTimeAsync(timeout.Token);
        return Results.Ok(new { status = "sent" });
    }

    private static async Task<IResult> SendAsync(DateTimeRequest value, HttpRequest request, IDittoProClient client, IHostApplicationLifetime lifetime)
    {
        if (value.Value is null) return Results.BadRequest(new { error = "value is required (ISO 8601 with UTC offset)." });
        using var timeout = DeviceOperationTimeout.Create(request, lifetime);
        await client.SendDateTimeAsync(value.Value.Value, timeout.Token);
        return Results.Ok(new { status = "sent" });
    }
}
