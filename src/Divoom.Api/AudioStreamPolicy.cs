namespace Divoom.Api;

/// <summary>Only trusted HTTPS hosts or explicitly allowed HTTP URLs may be handed to the OS media engine.</summary>
internal sealed class AudioStreamPolicy(IConfiguration configuration)
{
    private readonly HashSet<string> allowedHosts = new(
        (configuration["DIVOOM_AUDIO_ALLOWED_HOSTS"] ?? "").Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries),
        StringComparer.OrdinalIgnoreCase);

    private readonly HashSet<string> allowedHttpUrls = new(
        (configuration["DIVOOM_AUDIO_ALLOWED_HTTP_URLS"] ?? "")
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Where(value => value.Length <= 2048)
            .Select(value => Uri.TryCreate(value, UriKind.Absolute, out var uri) ? uri : null)
            .Where(uri => uri is not null && uri.Scheme == Uri.UriSchemeHttp &&
                string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Fragment))
            .Select(uri => uri!.AbsoluteUri),
        StringComparer.Ordinal);

    public Uri Validate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 2048 ||
            !Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment) ||
            !(uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 &&
                uri.HostNameType == UriHostNameType.Dns && allowedHosts.Contains(uri.IdnHost) ||
              uri.Scheme == Uri.UriSchemeHttp && allowedHttpUrls.Contains(uri.AbsoluteUri)))
            throw new ArgumentException("Use an HTTPS stream URL on a configured trusted station host or an explicitly configured HTTP stream URL.");
        return uri;
    }

    public static double ValidateVolume(double? volume, double? defaultValue)
    {
        var value = volume ?? defaultValue ?? throw new ArgumentException("Volume is required.");
        if (!double.IsFinite(value) || value is < 0 or > 1)
            throw new ArgumentException("Volume must be between 0 and 1.");
        return value;
    }
}
