using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Divoom;
using Divoom.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NUnit.Framework;

public sealed class ApiTests
{
    private const string key = "integration-test-key";
    private static readonly string[] settings = ["--DIVOOM_API_KEY", key, "--DIVOOM_ADDRESS", "B1:21:81:4B:E6:42"];
    private FakeClient device = null!;
    private WebApplication app = null!;
    private HttpClient http = null!;
    private int creations;

    [SetUp]
    public async Task SetUp()
    {
        device = new FakeClient();
        creations = 0;
        app = ApiHost.Build(settings, builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<IDittoProClient>(_ => { creations++; return device; });
        });
        await app.StartAsync();
        http = app.GetTestClient();
    }

    [TearDown]
    public async Task TearDown()
    {
        http.Dispose();
        await app.DisposeAsync();
        Assert.That(device.Disposals, Is.EqualTo(creations), "Host shutdown disposes the resolved device client exactly once.");
    }

    [Test]
    public async Task MissingApiKeyFailsStartup()
    {
        await Assert.ThatAsync(async () =>
        {
            await using var invalid = ApiHost.Build(["--DIVOOM_API_KEY", "", "--DIVOOM_ADDRESS", "B1:21:81:4B:E6:42"]);
        }, Throws.TypeOf<InvalidOperationException>().With.Message.Contains("DIVOOM_API_KEY"));
    }

    [Test]
    public async Task UnauthorizedRequestsDoNotResolveDevice()
    {
        foreach (var route in new[] { "/api/health", "/api/images", "/api/animations", "/api/time", "/api/time/current", "/api/display/clock", "/unknown" })
        {
            using var request = new HttpRequestMessage(route == "/api/health" || route == "/unknown" ? HttpMethod.Get : HttpMethod.Post, route);
            await Status(await http.SendAsync(request), HttpStatusCode.Unauthorized);
        }
        using (var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/health"))
        {
            wrong.Headers.Add("X-Api-Key", "wrong");
            await Status(await http.SendAsync(wrong), HttpStatusCode.Unauthorized);
        }
        using (var duplicate = new HttpRequestMessage(HttpMethod.Get, "/api/health"))
        {
            duplicate.Headers.TryAddWithoutValidation("X-Api-Key", new[] { key, key });
            await Status(await http.SendAsync(duplicate), HttpStatusCode.Unauthorized);
        }
        await Status(await http.GetAsync($"/api/health?api_key={key}"), HttpStatusCode.Unauthorized);
        Assert.That(creations, Is.Zero, "Unauthorized calls resolved the device client.");
    }

    [Test]
    public async Task HealthAndUnknownRouteDoNotResolveDevice()
    {
        http.DefaultRequestHeaders.Add("X-Api-Key", key);
        await Status(await http.GetAsync("/api/health"), HttpStatusCode.OK);
        Assert.That(creations, Is.Zero, "Health opened a device client.");
        await Status(await http.GetAsync("/unknown"), HttpStatusCode.NotFound);
    }

    [Test]
    public async Task ValidOperationsAreForwardedAndReuseDevice()
    {
        http.DefaultRequestHeaders.Add("X-Api-Key", key);
        await Upload("/api/images", File.ReadAllBytes(Fixture("demo.png")), "image/png", HttpStatusCode.OK);
        Assert.That(device.Images, Is.EqualTo(1), "Image was not sent.");
        await Upload("/api/animations", File.ReadAllBytes(Fixture("mail-14.gif")), "image/gif", HttpStatusCode.OK);
        Assert.That(device.Frames, Is.EqualTo(9), "GIF frames were not preserved.");
        await Status(await http.PostAsync("/api/time/current", null), HttpStatusCode.OK);
        var value = DateTimeOffset.Parse("2026-10-06T12:34:56+02:00");
        await Status(await http.PostAsJsonAsync("/api/time", new { value }), HttpStatusCode.OK);
        Assert.That(device.CurrentTimes == 1 && device.Time == value, Is.True, "Date/time operations were not forwarded.");
        using (var clock = await http.PostAsync("/api/display/clock", null))
        {
            Assert.That(clock.StatusCode, Is.EqualTo(HttpStatusCode.OK), "Clock selection failed.");
            var body = await clock.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            Assert.That(body.GetProperty("status").GetString() == "sent" && body.GetProperty("mode").GetString() == "clock", Is.True, "Clock response contract.");
        }
        Assert.That(device.Clocks == 1 && device.CurrentTimes == 1 && device.Time == value, Is.True, "Clock selection must be forwarded separately from time synchronization.");
        Assert.That(creations == 1 && device.Disposals == 0, Is.True, "The device client was not reused.");
    }

    [Test]
    [TestCase("", 0, 0x00FF00u)]
    [TestCase("{}", 0, 0x00FF00u)]
    [TestCase("{\"style\":15}", 15, 0x00FF00u)]
    [TestCase("{\"color\":\"#123aBc\"}", 0, 0x123ABCu)]
    [TestCase("{\"style\":1,\"color\":\"#000000\"}", 1, 0u)]
    public async Task ClockAppearanceDefaultsAndOverrides(string json, int style, uint color)
    {
        http.DefaultRequestHeaders.Add("X-Api-Key", key);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync("/api/display/clock", content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(await response.Content.ReadAsStringAsync(), Is.EqualTo("{\"status\":\"sent\",\"mode\":\"clock\"}"));
        Assert.That(device.ClockStyle, Is.EqualTo(style));
        Assert.That(device.ClockColor, Is.EqualTo(color));
        Assert.That(device.Clocks, Is.EqualTo(1));
    }

    [TestCase("{\"style\":-1}")]
    [TestCase("{\"style\":16}")]
    [TestCase("{\"style\":1.5}")]
    [TestCase("{\"color\":null}")]
    [TestCase("{\"color\":\"00FF00\"}")]
    [TestCase("{\"color\":\"#GGFF00\"}")]
    [TestCase("{\"color\":\"#123\"}")]
    [TestCase("{\"color\":\"#00000000\"}")]
    [TestCase("{\"color\":123}")]
    [TestCase("{")]
    public async Task InvalidClockAppearanceDoesNotSendCommands(string json)
    {
        http.DefaultRequestHeaders.Add("X-Api-Key", key);
        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        await Status(await http.PostAsync("/api/display/clock", content), HttpStatusCode.BadRequest);
        Assert.That(device.Clocks, Is.Zero);
    }

    [Test]
    public async Task InvalidRequestsDoNotReachDevice()
    {
        http.DefaultRequestHeaders.Add("X-Api-Key", key);
        await Status(await http.PostAsJsonAsync("/api/time", new { }), HttpStatusCode.BadRequest);
        await Status(await http.PostAsync("/api/time", new StringContent("{broken", System.Text.Encoding.UTF8, "application/json")), HttpStatusCode.BadRequest);
        await Upload("/api/images", [], "image/png", HttpStatusCode.BadRequest);
        await Upload("/api/images", [1, 2, 3], "image/png", HttpStatusCode.BadRequest);
        await Upload("/api/images", [1], "text/plain", HttpStatusCode.UnsupportedMediaType);
        await Upload("/api/animations", File.ReadAllBytes(Fixture("demo.png")), "image/gif", HttpStatusCode.BadRequest);
        await Upload("/api/images", new byte[2 * 1024 * 1024 + 1], "image/png", HttpStatusCode.RequestEntityTooLarge);
        var oversized = File.ReadAllBytes(Fixture("mail-14.gif"));
        oversized[6] = oversized[7] = oversized[8] = oversized[9] = 255;
        await Upload("/api/animations", oversized, "image/gif", HttpStatusCode.BadRequest);
        Assert.That(device.Images == 0 && device.Frames == 0, Is.True, "Invalid media reached the device.");
    }

    [Test]
    public async Task TransportErrorsReturnSanitizedResponses()
    {
        http.DefaultRequestHeaders.Add("X-Api-Key", key);
        device.Error = new IOException("private transport details");
        await Status(await http.PostAsync("/api/display/clock", null), HttpStatusCode.ServiceUnavailable, "private transport details");
        await Status(await http.PostAsync("/api/time/current", null), HttpStatusCode.ServiceUnavailable, "private transport details");
        device.Error = new TimeoutException("private timeout details");
        await Status(await http.PostAsync("/api/display/clock", null), HttpStatusCode.GatewayTimeout, "private timeout details");
        await Status(await http.PostAsync("/api/time/current", null), HttpStatusCode.GatewayTimeout, "private timeout details");
        device.Error = null;
    }

    [Test]
    public async Task KestrelEnforcesAuthenticationAndBodyLimit()
    {
        await using (var network = ApiHost.Build(settings, builder =>
        {
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<IDittoProClient>(_ => new FakeClient());
        }))
        {
            await network.StartAsync();
            var address = network.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var http = new HttpClient { BaseAddress = new Uri(address) };
            await Status(await http.GetAsync("/api/health"), HttpStatusCode.Unauthorized);
            http.DefaultRequestHeaders.Add("X-Api-Key", key);
            await Status(await http.GetAsync("/api/health"), HttpStatusCode.OK);
            await Status(await http.PostAsync("/api/time/current", null), HttpStatusCode.OK);
            using var large = new ByteArrayContent(new byte[2 * 1024 * 1024 + 1]);
            large.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            await Status(await http.PostAsync("/api/images", large), HttpStatusCode.RequestEntityTooLarge);
            await network.StopAsync();
        }
    }

    private async Task Upload(string path, byte[] bytes, string contentType, HttpStatusCode expected)
    {
        using var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        await Status(await http.PostAsync(path, content), expected);
    }

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "fixtures", name);
    private static async Task Status(HttpResponseMessage response, HttpStatusCode expected, string? forbidden = null)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            Assert.That(response.StatusCode, Is.EqualTo(expected), $"Expected {(int)expected}, got {(int)response.StatusCode}: {body}");
            Assert.That(forbidden is null || !body.Contains(forbidden), Is.True, "HTTP response leaked private exception details.");
        }
    }
}
