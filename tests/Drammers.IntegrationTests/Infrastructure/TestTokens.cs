using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Drammers.IntegrationTests.Infrastructure;

/// <summary>Tokens zoals Entra External ID die uitgeeft, ondertekend met een lokale testsleutel.</summary>
public static class TestTokens
{
    public const string Issuer = "https://test.ciamlogin.com/tenant/v2.0";
    public const string DevAudience = "api-dev-client-id";
    public const string ProdAudience = "api-prod-client-id";

    public static readonly SecurityKey SigningKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" };

    public static string Create(
        string objectId,
        string? environmentAccess = "dev",
        string audience = DevAudience,
        string issuer = Issuer,
        DateTime? expires = null)
    {
        var claims = new List<Claim>
        {
            new("oid", objectId),
            new("sub", objectId),
            new("uti", Guid.NewGuid().ToString("N")),
            new("name", "Test"),
        };
        if (environmentAccess is not null)
        {
            claims.Add(new Claim("environmentAccess", environmentAccess));
        }

        var expiry = expires ?? DateTime.UtcNow.AddMinutes(5);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            NotBefore = expiry.AddMinutes(-10),
            IssuedAt = expiry.AddMinutes(-10),
            Expires = expiry,
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }
}
