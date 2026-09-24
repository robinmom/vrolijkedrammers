using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Drammers.ApiTests.Infrastructure;

/// <summary>Maakt tokens zoals Entra External ID die uitgeeft, ondertekend met een lokale testsleutel.</summary>
public static class TestTokens
{
    public const string Issuer = "https://test.ciamlogin.com/tenant/v2.0";
    public const string DevAudience = "api-dev-client-id";
    public const string ProdAudience = "api-prod-client-id";

    public static readonly SecurityKey SigningKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-key" };

    public static string Create(string audience, string? environmentAccess)
    {
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()), new("name", "Test Carnavalist") };
        if (environmentAccess is not null)
        {
            claims.Add(new Claim("environmentAccess", environmentAccess));
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(5),
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
        });
    }
}
