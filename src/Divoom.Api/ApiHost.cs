using System.Security.Cryptography;
using System.Text;
using Divoom;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Divoom.Api.Endpoints;

namespace Divoom.Api;

public static class ApiHost
{
    public static WebApplication Build(string[] args, Action<WebApplicationBuilder>? configure = null)
    {
        var builder = WebApplication.CreateBuilder(args);
        var key = builder.Configuration["DIVOOM_API_KEY"];
        if (string.IsNullOrWhiteSpace(key)) throw new InvalidOperationException("DIVOOM_API_KEY must be configured.");
        var address = builder.Configuration["DIVOOM_ADDRESS"];
        if (string.IsNullOrWhiteSpace(address)) throw new InvalidOperationException("DIVOOM_ADDRESS must be configured.");
        builder.WebHost.ConfigureKestrel(server => server.Limits.MaxRequestBodySize = MediaUpload.MaxBytes);
        builder.Services.AddSingleton(new ApiKeyCredential(SHA256.HashData(Encoding.UTF8.GetBytes(key))));
        builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization(options => options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser().Build());
        builder.Services.AddProblemDetails();
        builder.Services.AddSingleton<IDittoProClient>(services =>
        {
            var directory = builder.Configuration["DIVOOM_STATE_DIRECTORY"] ?? (OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Divoom.Api")
                : "/data");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "controller-id.txt");
            var controllerId = File.Exists(path) ? Guid.Parse(File.ReadAllText(path)) : Guid.NewGuid();
            if (!File.Exists(path)) File.WriteAllText(path, controllerId.ToString());
            var logger = services.GetRequiredService<ILogger<DittoProClient>>();
            return new DittoProClient(DeviceTransportFactory.Create(address, builder.Configuration), new DittoProClientOptions
            {
                ControllerId = controllerId,
                TimeZone = TimeZoneInfo.FindSystemTimeZoneById(builder.Configuration["TZ"] ?? "Europe/Amsterdam"),
                Log = message => logger.LogInformation("{DeviceMessage}", message)
            });
        });
        // Hosts/tests can replace the client implementation without changing HTTP routes.
        configure?.Invoke(builder);
        var app = builder.Build();
        app.UseExceptionHandler(errors => errors.Run(ApiExceptionResponse.WriteAsync));
        app.UseAuthentication();
        app.UseAuthorization();
        var api = app.MapGroup("/api").RequireAuthorization();
        HealthEndpoints.Map(api);
        ImageEndpoints.Map(api);
        AnimationEndpoints.Map(api);
        TimeEndpoints.Map(api);
        DisplayEndpoints.Map(api);
        return app;
    }
}
