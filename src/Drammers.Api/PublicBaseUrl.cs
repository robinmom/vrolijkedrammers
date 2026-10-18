namespace Drammers.Api;

/// <summary>
/// Openbaar adres voor links in e-mails en betaallinks (Mollie redirect en webhook): vast uit <c>Sales:PublicBaseUrl</c>
/// (Bicep zet dit per omgeving). Nooit de Host-header van het verzoek gebruiken buiten de lokale ontwikkeling en tests:
/// die bepaalt de afzender, en dan zou een link in een mail naar een willekeurig domein kunnen wijzen.
/// </summary>
public static class PublicBaseUrl
{
    public const string ConfigurationKey = "Sales:PublicBaseUrl";

    public static string PublicBaseUrlFor(this HttpRequest request)
    {
        var services = request.HttpContext.RequestServices;
        if (services.GetRequiredService<IConfiguration>()[ConfigurationKey] is { Length: > 0 } configured)
        {
            return configured.TrimEnd('/');
        }

        // Terugval op het verzoek alleen lokaal (Development) en in de tests; elders is het een configuratiefout.
        var environment = services.GetRequiredService<IHostEnvironment>();
        if (environment.IsDevelopment() || environment.IsEnvironment("Testing"))
        {
            return $"{request.Scheme}://{request.Host}";
        }

        throw new InvalidOperationException($"{ConfigurationKey} ontbreekt; zonder vast openbaar adres worden geen links verstuurd.");
    }
}
