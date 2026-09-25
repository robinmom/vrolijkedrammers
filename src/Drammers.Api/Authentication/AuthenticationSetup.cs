using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Drammers.Api.Authentication;

public static class AuthenticationSetup
{
    /// <summary>
    /// JWT-bearer-validatie tegen Entra External ID: handtekening, issuer, audience (per omgeving) en looptijd; anders 401.
    /// Zonder geconfigureerde audience faalt elke tokenvalidatie (fail closed).
    /// </summary>
    public static IServiceCollection AddDrammersAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(AuthOptions.SectionName);
        services.Configure<AuthOptions>(section);
        var options = section.Get<AuthOptions>() ?? new AuthOptions();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(jwt =>
            {
                jwt.Authority = string.IsNullOrWhiteSpace(options.Authority) ? null : options.Authority;
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters.ValidateAudience = true;
                jwt.TokenValidationParameters.ValidAudience = string.IsNullOrWhiteSpace(options.Audience) ? null : options.Audience;
                jwt.TokenValidationParameters.NameClaimType = "name";
            });

        // environmentAccess, account-status en permissions controleert de autorisatie (403), zie ActiveUserHandler.
        return services;
    }
}
