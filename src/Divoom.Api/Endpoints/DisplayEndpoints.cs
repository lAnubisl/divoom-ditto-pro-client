using Divoom;
using System.Globalization;

namespace Divoom.Api.Endpoints;

internal static class DisplayEndpoints
{
    public static void Map(RouteGroupBuilder api) => api.MapPost("/display/clock", ShowClockAsync);

    private static async Task<IResult> ShowClockAsync(ClockDisplayRequest? value, HttpRequest request, IDittoProClient client, IHostApplicationLifetime lifetime)
    {
        value ??= new();
        if (value.Style is < 0 or > 15)
            return Results.Problem(statusCode: 400, detail: "style must be an integer from 0 to 15.");
        if (value.Color is null || value.Color.Length != 7 || value.Color[0] != '#' ||
            !uint.TryParse(value.Color.AsSpan(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var color))
            return Results.Problem(statusCode: 400, detail: "color must be an RGB hex string in #RRGGBB format.");
        using var timeout = DeviceOperationTimeout.Create(request, lifetime);
        await client.ShowClockAsync((byte)value.Style, color, timeout.Token);
        return Results.Ok(new { status = "sent", mode = "clock" });
    }
}
