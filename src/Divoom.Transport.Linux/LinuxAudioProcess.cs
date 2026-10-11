using System.Diagnostics;
using System.Globalization;

namespace Divoom;

internal static class LinuxAudioProcess
{
    private static readonly HashSet<string> AllowedEnvironment = new(StringComparer.Ordinal)
    {
        "PATH", "HOME", "TMPDIR", "LANG", "LC_ALL", "LC_CTYPE", "TZ", "LD_LIBRARY_PATH",
        "XDG_RUNTIME_DIR", "PULSE_SERVER", "PULSE_COOKIE", "PULSE_CLIENTCONFIG", "PULSE_PROP",
        "SSL_CERT_FILE", "SSL_CERT_DIR", "http_proxy", "https_proxy", "all_proxy", "no_proxy",
        "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "NO_PROXY"
    };

    public static ProcessStartInfo CreateStartInfo(string device, double volume, string socketPath,
        IEnumerable<KeyValuePair<string, string?>> environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var start = new ProcessStartInfo("mpv")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in new[] {
            "--no-config", "--idle=yes", "--no-video", "--no-terminal", "--input-terminal=no", "--ytdl=no",
            "--tls-verify=yes", "--audio-fallback-to-null=no", "--audio-device=" + device,
            "--volume=" + (volume * 100).ToString(CultureInfo.InvariantCulture),
            "--input-ipc-server=" + socketPath, "--msg-level=all=no",
            "--demuxer-max-bytes=4194304", "--demuxer-max-back-bytes=1048576", "--cache-secs=5"
        }) start.ArgumentList.Add(argument);

        var retained = environment.Where(pair => AllowedEnvironment.Contains(pair.Key)).ToArray();
        start.Environment.Clear();
        foreach (var pair in retained) start.Environment[pair.Key] = pair.Value;
        // Keep PipeWire from moving this stream to another device when its target disappears.
        start.Environment.TryGetValue("PULSE_PROP", out var properties);
        start.Environment["PULSE_PROP"] = string.IsNullOrWhiteSpace(properties)
            ? "node.dont-reconnect=true" : properties + " node.dont-reconnect=true";
        return start;
    }
}
