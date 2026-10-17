using System.Net;
using System.Net.Sockets;
using System.Text;
using Drammers.Api.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Drammers.Api.Controllers;

/// <summary>
/// Aanmelden in de app (fase 9): de app haalt hier de omgevingsspecifieke instellingen op (geen geheimen), zodat
/// dezelfde build in Dev, Acc en Prod werkt. Inloggen zelf: OIDC + PKCE via de systeembrowser met e-mailcode.
/// </summary>
[ApiController]
[AllowAnonymous]
public sealed class AppAuthController(IOptions<AuthOptions> auth) : ControllerBase
{
    public const string BridgePath = "app/auth-redirect";

    [HttpGet("api/v1/app-auth-config")]
    [ProducesResponseType<AppAuthConfigResponse>(StatusCodes.Status200OK)]
    public AppAuthConfigResponse GetConfig()
    {
        var options = auth.Value;
        var bridge = options.MobileRedirectBridge ? $"{Request.Scheme}://{Request.Host}/{BridgePath}" : null;
        return new AppAuthConfigResponse(
            options.MobileClientId ?? string.Empty,
            options.Authority ?? string.Empty,
            ["openid", "offline_access", $"api://{options.Audience}/access_as_user"],
            bridge);
    }

    /// <summary>
    /// Doorstuurpagina voor Expo Go (alleen Dev/Acc): Entra stuurt de code hierheen en deze pagina geeft hem door aan
    /// het adres dat de app in de <c>state</c> meegaf. Alleen <c>drammers://</c> en <c>exp://</c>/<c>exps://</c> naar de
    /// eigen computer of een privé-LAN-adres (de Expo-ontwikkelserver) zijn toegestaan: geen open redirect naar het web of
    /// naar een Expo-server van een ander. De code is zonder de PKCE-verifier van de app waardeloos.
    /// </summary>
    [HttpGet(BridgePath)]
    [ApiExplorerSettings(IgnoreApi = true)]
    public IActionResult Bridge([FromQuery] string? state)
    {
        if (!auth.Value.MobileRedirectBridge || ReturnUrl(state) is not { } target)
        {
            return NotFound();
        }

        var separator = target.Contains('?', StringComparison.Ordinal) ? "&" : "?";
        return Redirect(target + separator + Request.QueryString.Value?.TrimStart('?'));
    }

    /// <summary>De state is <c>{willekeurig}.{base64url(terugkeeradres)}</c>.</summary>
    public static string? ReturnUrl(string? state)
    {
        var dot = state?.LastIndexOf('.') ?? -1;
        if (state is null || dot < 0)
        {
            return null;
        }

        try
        {
            var encoded = state[(dot + 1)..].Replace('-', '+').Replace('_', '/');
            encoded = encoded.PadRight(encoded.Length + ((4 - (encoded.Length % 4)) % 4), '=');
            var url = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
            return Uri.TryCreate(url, UriKind.Absolute, out var uri) && IsAllowed(uri) ? url : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private static bool IsAllowed(Uri uri) => uri.Scheme switch
    {
        "drammers" => true,
        "exp" or "exps" => IsLocalDevelopmentHost(uri),
        _ => false,
    };

    /// <summary>Localhost of een IP-adres (geen hostnaam) op de eigen computer of in een privé-LAN (10/8, 172.16/12, 192.168/16).</summary>
    private static bool IsLocalDevelopmentHost(Uri uri)
    {
        if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (uri.HostNameType is not (UriHostNameType.IPv4 or UriHostNameType.IPv6) || !IPAddress.TryParse(uri.DnsSafeHost, out var address))
        {
            return false;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address))
        {
            return true;
        }

        if (address.AddressFamily != AddressFamily.InterNetwork)
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        return bytes[0] == 10
            || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
            || (bytes[0] == 192 && bytes[1] == 168);
    }
}

public sealed record AppAuthConfigResponse(string ClientId, string Authority, IReadOnlyList<string> Scopes, string? RedirectBridgeUrl);
