using Microsoft.AspNetCore.Diagnostics;

namespace Divoom.Api;

internal static class ApiExceptionResponse
{
    public static Task WriteAsync(HttpContext context)
    {
        var error = context.Features.Get<IExceptionHandlerFeature>()!.Error;
        var (status, title) = error switch
        {
            BadHttpRequestException bad => (bad.StatusCode, bad.Message),
            InvalidDataException or ArgumentException => (400, "Invalid request or media file."),
            TimeoutException => (504, "Device operation timed out."),
            OperationCanceledException when context.RequestAborted.IsCancellationRequested => (499, "Request cancelled."),
            OperationCanceledException => (504, "Device operation timed out or the service is stopping."),
            IOException or Tmds.DBus.DBusException or Tmds.DBus.DisconnectedException => (503, "Bluetooth device is unavailable."),
            _ => (500, "An unexpected server error occurred.")
        };
        return Results.Problem(statusCode: status, title: title).ExecuteAsync(context);
    }
}
