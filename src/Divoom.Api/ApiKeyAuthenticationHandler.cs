using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Divoom.Api;

public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
    UrlEncoder encoder, ApiKeyCredential credential)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiKey";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Api-Key", out var values))
            return Task.FromResult(AuthenticateResult.NoResult());
        if (values.Count != 1 || string.IsNullOrEmpty(values[0]))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(values[0]!));
        if (!CryptographicOperations.FixedTimeEquals(credential.Hash, suppliedHash))
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "api-key")], SchemeName);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.Headers.WWWAuthenticate = SchemeName;
        return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "A valid X-Api-Key header is required.").ExecuteAsync(Context);
    }
}
