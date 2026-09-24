using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Drammers.ApiTests.Infrastructure;

/// <summary>API zoals in een omgeving met Entra-configuratie, maar met een lokale sleutel in plaats van de OIDC-metadata.</summary>
public class AuthApiFactory(string? requiredEnvironmentAccess) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Auth:Audience", TestTokens.DevAudience);
        builder.UseSetting("Auth:RequiredEnvironmentAccess", requiredEnvironmentAccess ?? string.Empty);
        builder.ConfigureTestServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                options.Authority = null;
                options.TokenValidationParameters.ValidIssuer = TestTokens.Issuer;
                options.TokenValidationParameters.IssuerSigningKey = TestTokens.SigningKey;
            }));
    }
}

public sealed class DevApiFactory() : AuthApiFactory("dev");

public sealed class ProdApiFactory() : AuthApiFactory(null);
