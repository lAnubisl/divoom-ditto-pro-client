namespace Divoom.Api;

/// <summary>Only trusted station hosts may be handed to the OS media engine.</summary>
internal sealed class AudioStreamPolicy(IConfiguration configuration)
{
    private readonly HashSet<string> allowedHosts = new(
        (configuration["DIVOOM_AUDIO_ALLOWED_HOSTS"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
        StringComparer.OrdinalIgnoreCase);

    public Uri Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) ||
            uri.HostNameType != UriHostNameType.Dns || !allowedHosts.Contains(uri.IdnHost))
            throw new ArgumentException("Use an HTTPS stream URL on a configured trusted station host.");
        return uri;
    }

    public static double ValidateVolume(double? volume, double? defaultValue = null)
    {
        var value = volume ?? defaultValue ?? throw new ArgumentException("Volume is required.");
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentException("Volume must be between 0 and 1.");
        return value;
    }
}
