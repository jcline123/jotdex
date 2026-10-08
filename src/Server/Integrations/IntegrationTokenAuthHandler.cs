using System.Security.Claims;
using System.Text.Encodings.Web;
using Jotdex.Core.Integrations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Jotdex.Server.Integrations;

public sealed class IntegrationTokenAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    private readonly IIntegrationTokenStore _tokens;
    private readonly IIntegrationConfigService _config;
    private readonly IVaultIdentity _vaultIdentity;
    private readonly TimeProvider _time;

    public IntegrationTokenAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IIntegrationTokenStore tokens,
        IIntegrationConfigService config,
        IVaultIdentity vaultIdentity,
        TimeProvider time)
        : base(options, logger, encoder)
    {
        _tokens = tokens;
        _config = config;
        _vaultIdentity = vaultIdentity;
        _time = time;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!_config.IsFeatureEnabled())
            return Task.FromResult(AuthenticateResult.Fail("Integrations are disabled"));

        var header = Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(header) ||
            !header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return Task.FromResult(AuthenticateResult.NoResult());

        var secret = header["Bearer ".Length..].Trim();
        if (secret.Length == 0 || !secret.StartsWith(IntegrationAuth.TokenPrefix, StringComparison.Ordinal))
            return Task.FromResult(AuthenticateResult.Fail("Invalid token"));

        var vaultId = _vaultIdentity.GetVaultId();
        if (string.IsNullOrWhiteSpace(vaultId))
            return Task.FromResult(AuthenticateResult.Fail("Vault identity unavailable"));

        IntegrationTokenRecord? record;
        try
        {
            record = _tokens.Authenticate(secret, vaultId, _time);
        }
        catch
        {
            return Task.FromResult(AuthenticateResult.Fail("Token store unavailable"));
        }

        if (record is null)
            return Task.FromResult(AuthenticateResult.Fail("Invalid or expired token"));

        _tokens.TouchLastUsed(record.Id, _time);

        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, record.Name),
            new(ClaimTypes.NameIdentifier, record.Id),
            new(IntegrationAuth.ClaimTokenId, record.Id),
            new(IntegrationAuth.ClaimTokenName, record.Name),
            new(IntegrationAuth.ClaimVaultId, record.VaultId),
            new(IntegrationAuth.ClaimWholeVault, record.WholeVault ? "1" : "0"),
            new(IntegrationAuth.ClaimExpiresAt, record.ExpiresAt.ToString("O")),
            new(IntegrationAuth.ClaimScopes, string.Join(' ', record.Scopes)),
            new(IntegrationAuth.ClaimFolders, string.Join('\n', record.AllowedFolderRoots))
        };

        var identity = new ClaimsIdentity(claims, IntegrationAuth.Scheme);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, IntegrationAuth.Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
