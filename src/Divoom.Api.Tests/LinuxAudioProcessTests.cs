using Divoom;
using NUnit.Framework;

public sealed class LinuxAudioProcessTests
{
    [Test]
    public void PlayerVerifiesTlsAndKeepsArgumentsSeparate()
    {
        var start = LinuxAudioProcess.CreateStartInfo("pulse/sink with spaces", 0.6, "/private path/ipc", []);
        Assert.That(start.UseShellExecute, Is.False);
        Assert.That(start.ArgumentList, Does.Contain("--tls-verify=yes"));
        Assert.That(start.ArgumentList, Does.Contain("--audio-fallback-to-null=no"));
        Assert.That(start.ArgumentList, Does.Contain("--audio-device=pulse/sink with spaces"));
        Assert.That(start.ArgumentList, Does.Contain("--input-ipc-server=/private path/ipc"));
        Assert.That(start.ArgumentList, Does.Contain("--volume=60"));
    }

    [Test]
    public void PlayerReceivesOnlyExplicitAudioAndNetworkEnvironment()
    {
        var input = new Dictionary<string, string?>
        {
            ["PATH"] = "/usr/bin", ["HOME"] = "/example", ["PULSE_SERVER"] = "unix:/example/native",
            ["PULSE_CLIENTCONFIG"] = "/example/client.conf", ["PULSE_PROP"] = "application.name=example",
            ["HTTPS_PROXY"] = "http://proxy.example:8080", ["SSL_CERT_FILE"] = "/example/ca.pem",
            ["DIVOOM_API_KEY"] = "synthetic-key", ["DIVOOM_ADDRESS"] = "synthetic-address",
            ["ASPNETCORE_HTTP_PORTS"] = "8080", ["DOTNET_DIAGNOSTICS"] = "synthetic-value",
            ["UNRELATED_SECRET"] = "synthetic-secret"
        };
        var start = LinuxAudioProcess.CreateStartInfo("pulse/example", 0.25, "/example/ipc", input);
        Assert.That(start.Environment.Keys, Is.EquivalentTo(new[]
            { "PATH", "HOME", "PULSE_SERVER", "PULSE_CLIENTCONFIG", "PULSE_PROP", "HTTPS_PROXY", "SSL_CERT_FILE" }));
        Assert.That(start.Environment["PULSE_SERVER"], Is.EqualTo(input["PULSE_SERVER"]));
        Assert.That(start.Environment["PULSE_PROP"], Is.EqualTo("application.name=example node.dont-reconnect=true"));
    }
}
