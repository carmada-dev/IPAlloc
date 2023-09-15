using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using Microsoft.IdentityModel.Tokens;

namespace IPAlloc.Middleware;

internal sealed class ClaimsPrincipalFeature
{
    private static readonly JwtSecurityTokenHandler SecurityTokenHandler = new JwtSecurityTokenHandler();

    private static JwtSecurityToken? ParseSecurityToken(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        return SecurityTokenHandler.ReadJwtToken(token);
    }

    public static readonly ClaimsPrincipalFeature Empty = new ClaimsPrincipalFeature();

    public ClaimsPrincipal? Principal { get; init; }

    public string? Token { get; init; }

    public Guid? GetObjectId()
        => Principal?.GetObjectId() ?? ParseSecurityToken(Token)?.GetObjectId();

    public Guid? GetTenantId()
        => Principal?.GetTenantId() ?? ParseSecurityToken(Token)?.GetTenantId();

    public Guid? GetClientId()
        => Principal?.GetClientId() ?? ParseSecurityToken(Token)?.GetClientId();
}
