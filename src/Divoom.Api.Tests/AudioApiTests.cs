using System.Net;
using System.Net.Http.Json;
using System.Text;
using Divoom;
using Divoom.Api;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

public sealed class AudioApiTests
{
    private const string Route = "/api/audio/playback";
    private static readonly string[] Settings = ["--DIVOOM_API_KEY", "audio-test-key", "--DIVOOM_ADDRESS", "B1:21:81:4B:E6:42", "--DIVOOM_AUDIO_ALLOWED_HOSTS", "radio.example"];
    private FakeAudioBackend backend = null!;
    private WebApplication app = null!;
    private HttpClient http = null!;

    [SetUp]
    public Task SetUp() => StartHostAsync();

    private async Task StartHostAsync(string allowedHttpUrls = "", string allowedHosts = "radio.example")
    {
        backend = new FakeAudioBackend();
        app = ApiHost.Build(Settings, builder =>
        {
            builder.Configuration["DIVOOM_AUDIO_ALLOWED_HTTP_URLS"] = allowedHttpUrls;
            builder.Configuration["DIVOOM_AUDIO_ALLOWED_HOSTS"] = allowedHosts;
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<IDivoomAudioPlayer>(_ => new DivoomAudioPlayer(backend, recovery: new() { Enabled = false }, startupTimeout: TimeSpan.FromSeconds(20), reportRecovery: null));
        });
        await app.StartAsync();
        http = app.GetTestClient();
    }

    private async Task ConfigureHttpStreamsAsync(string urls, string allowedHosts = "radio.example")
    {
        http.Dispose();
        await app.DisposeAsync();
        await StartHostAsync(urls, allowedHosts);
    }

    [TearDown]
    public async Task TearDown()
    {
        http.Dispose();
        await app.DisposeAsync();
        foreach (var session in backend.Sessions) Assert.That(session.Disposals, Is.EqualTo(1));
    }

    [Test]
    public async Task AllAudioRoutesRequireAuthentication()
    {
        foreach (var method in new[] { HttpMethod.Get, HttpMethod.Put, HttpMethod.Patch, HttpMethod.Delete })
        {
            using var request = new HttpRequestMessage(method, Route) { Content = JsonContent.Create(new { url = "https://radio.example/live", volume = 0.25 }) };
            using var response = await http.SendAsync(request);
            Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Unauthorized));
        }
        Assert.That(backend.Sessions, Is.Empty);
    }

    [Test]
    public async Task StartStatusVolumeStopAndHostShutdownOwnPlayback()
    {
        http.DefaultRequestHeaders.Add("X-Api-Key", "audio-test-key");
        using (var start = await http.PutAsJsonAsync(Route, new { url = "https://radio.example/live" }))
        {
            Assert.That(start.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
            Assert.That(start.Headers.Location?.ToString(), Is.EqualTo(Route));
        }
        Assert.That(await http.GetFromJsonAsync<AudioPlaybackStatus>(Route), Is.EqualTo(new AudioPlaybackStatus("playing", 0.25, Error: null, RetryAttempts: 0, LastError: null)));
        using (var volume = await http.PatchAsJsonAsync(Route, new { volume = 0.4 })) Assert.That(volume.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That(backend.Sessions[0].Volume, Is.EqualTo(0.4));
        using (var stop = await http.DeleteAsync(Route)) Assert.That(stop.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await http.GetFromJsonAsync<AudioPlaybackStatus>(Route))!.State, Is.EqualTo("stopped"));
        using (var start = await http.PutAsJsonAsync(Route, new { url = "https://radio.example/other" })) Assert.That(start.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        await app.StopAsync();
        Assert.That(backend.Sessions.All(s => s.Disposals == 1), Is.True);
    }

    [TestCase("{}")]
    [TestCase("{\"url\":\"http://radio.example/live\"}")]
    [TestCase("{\"url\":\"http://192.168.100.112:8000/listening\"}")]
    [TestCase("{\"url\":\"file:///private.wav\"}")]
    [TestCase("{\"url\":\"https://other.example/live\"}")]
    [TestCase("{\"url\":\"https://radio.example.evil/live\"}")]
    [TestCase("{\"url\":\"https://127.0.0.1/live\"}")]
    [TestCase("{\"url\":\"https://radio.example:8443/live\"}")]
    [TestCase("{\"url\":\"https://user:password@radio.example/live\"}")]
    [TestCase("{\"url\":\"https://radio.example/live#fragment\"}")]
    [TestCase("{\"url\":\"https://radio.example/live\",\"volume\":-0.1}")]
    [TestCase("{\"url\":\"https://radio.example/live\",\"volume\":1.1}")]
    [TestCase("{")]
    public async Task InvalidStartDoesNotOpenAudio(string json)
    {
        http.DefaultRequestHeaders.Add("X-Api-Key", "audio-test-key");
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await http.PutAsync(Route, content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(backend.Sessions, Is.Empty);
    }

    [TestCase("http://192.168.100.112:8000/listening")]
    [TestCase("http://radio.example/live?station=1")]
    [TestCase("http://[fd00::112]:8000/listening")]
    public async Task ExplicitlyConfiguredHttpUrlReachesPlaybackBackend(string url)
    {
        await ConfigureHttpStreamsAsync(
            " http://192.168.100.112:8000/listening, ,http://radio.example/live?station=1, http://[fd00::112]:8000/listening ", allowedHosts: "");
        http.DefaultRequestHeaders.Add("X-Api-Key", "audio-test-key");

        using var response = await http.PutAsJsonAsync(Route, new { url, volume = 0.4 });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        Assert.That(backend.Source, Is.EqualTo(new Uri(url)));
        Assert.That(backend.Sessions.Single().Volume, Is.EqualTo(0.4));
    }

    [TestCase("http://192.168.100.113:8000/listening")]
    [TestCase("http://192.168.100.112:8001/listening")]
    [TestCase("http://192.168.100.112:8000/other")]
    [TestCase("http://192.168.100.112:8000/Listening")]
    [TestCase("http://192.168.100.112:8000/listening?other=1")]
    [TestCase("http://192.168.100.112:8000/listening#fragment")]
    [TestCase("http://user:password@192.168.100.112:8000/listening")]
    [TestCase("http://192.168.100.112.evil:8000/listening")]
    [TestCase("https://192.168.100.112:8000/listening")]
    [TestCase("file:///private.wav")]
    public async Task HttpAllowlistRequiresTheConfiguredUrl(string url)
    {
        await ConfigureHttpStreamsAsync(
            "http://192.168.100.112:8000/listening,file:///private.wav,http://user:password@192.168.100.112:8000/listening,http://192.168.100.112:8000/listening#fragment");
        http.DefaultRequestHeaders.Add("X-Api-Key", "audio-test-key");

        using var response = await http.PutAsJsonAsync(Route, new { url });

        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That(backend.Sessions, Is.Empty);
    }

    [TestCase("{}")]
    [TestCase("{\"volume\":null}")]
    [TestCase("{\"volume\":2}")]
    public async Task InvalidVolumeDoesNotChangePlayback(string json)
    {
        http.DefaultRequestHeaders.Add("X-Api-Key", "audio-test-key");
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await http.PatchAsync(Route, content);
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.BadRequest));
        Assert.That((await http.GetFromJsonAsync<AudioPlaybackStatus>(Route))!.Volume, Is.EqualTo(0.25));
    }

    [Test]
    public async Task MissingOutputReturnsSanitizedProblem()
    {
        http.DefaultRequestHeaders.Add("X-Api-Key", "audio-test-key");
        backend.Error = new AudioUnavailableException("private audio details");
        using var response = await http.PutAsJsonAsync(Route, new { url = "https://radio.example/live" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
        Assert.That(response.Content.Headers.ContentType?.MediaType, Is.EqualTo("application/problem+json"));
        Assert.That(await response.Content.ReadAsStringAsync(), Does.Not.Contain("private audio details"));
    }

    [TestCase("", "audio_not_configured")]
    [TestCase("Headphones (DitooPro-Audio)", "platform_unsupported")]
    public async Task AudioConfigurationDoesNotPreventPortableApiStartup(string output, string reason)
    {
        await using var disabled = ApiHost.Build(Settings, builder =>
        {
            builder.Configuration["DIVOOM_AUDIO_DEVICE"] = output;
            builder.Configuration["DIVOOM_AUDIO_AUTO_RECONNECT"] = "false";
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
        });
        await disabled.StartAsync();
        using var client = disabled.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "audio-test-key");
        var expected = OperatingSystem.IsLinux() && output.Length > 0
            ? new AudioPlaybackStatus("stopped", 0.25, Error: null, RetryAttempts: 0, LastError: null)
            : new AudioPlaybackStatus("unavailable", 0.25, reason, RetryAttempts: 0, LastError: null);
        Assert.That(await client.GetFromJsonAsync<AudioPlaybackStatus>(Route), Is.EqualTo(expected));
        using var response = await client.PutAsJsonAsync(Route, new { url = "https://radio.example/live" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.ServiceUnavailable));
    }

    [Test]
    public async Task MissingOutputWithRecoveryAcceptsStartAndStopCancelsIt()
    {
        backend.Error = new AudioUnavailableException("private output detail");
        await using var recovering = ApiHost.Build(Settings, builder =>
        {
            builder.WebHost.UseTestServer();
            builder.Logging.ClearProviders();
            builder.Services.AddSingleton<IDivoomAudioPlayer>(_ => new DivoomAudioPlayer(backend, startupTimeout: TimeSpan.FromSeconds(20), recovery: new AudioRecoveryOptions(), reportRecovery: null));
        });
        await recovering.StartAsync();
        using var client = recovering.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "audio-test-key");
        using var response = await client.PutAsJsonAsync(Route, new { url = "https://radio.example/live" });
        Assert.That(response.StatusCode, Is.EqualTo(HttpStatusCode.Accepted));
        var json = await response.Content.ReadAsStringAsync();
        Assert.That(json, Does.Not.Contain("private output detail").And.Not.Contain("radio.example"));
        var status = await client.GetFromJsonAsync<AudioPlaybackStatus>(Route);
        Assert.That(status!.State, Is.EqualTo("reconnecting"));
        Assert.That(status.LastError, Is.EqualTo("output_unavailable"));
        using var stop = await client.DeleteAsync(Route);
        Assert.That(stop.StatusCode, Is.EqualTo(HttpStatusCode.OK));
        Assert.That((await client.GetFromJsonAsync<AudioPlaybackStatus>(Route))!.State, Is.EqualTo("stopped"));
    }
}
