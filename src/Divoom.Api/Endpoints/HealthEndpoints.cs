namespace Divoom.Api.Endpoints;

internal static class HealthEndpoints
{
    public static void Map(RouteGroupBuilder api) => api.MapGet("/health", Handle);

    private static IResult Handle() => Results.Ok(new { status = "ok" });
}
