using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace Drammers.Api.Authentication;

public static class AuthenticationSetup
{
    /// <summary>
    /// JWT-bearer-validatie tegen Entra External ID. Zonder geconfigureerde audience faalt elke tokenvalidatie
    /// (fail closed), zodat een ontbrekende instelling nooit tot open toegang leidt.
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
                jwt.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (!EnvironmentAccess.IsAllowed(context.Principal!, options.EnvironmentAccessClaim, options.RequiredEnvironmentAccess))
                        {
                            context.Fail("Het token geeft geen toegang tot deze omgeving.");
                        }

                        return Task.CompletedTask;
                    },
                };
            });

        services.AddAuthorization();
        return services;
    }
}
