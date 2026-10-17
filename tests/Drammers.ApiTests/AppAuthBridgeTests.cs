using System.Text;
using Drammers.Api.Controllers;

namespace Drammers.ApiTests;

/// <summary>Doorstuurpagina voor Expo Go: alleen de eigen app of een Expo-ontwikkelserver op de eigen computer of het LAN.</summary>
public class AppAuthBridgeTests
{
    private static string State(string url) =>
        "abc." + Convert.ToBase64String(Encoding.UTF8.GetBytes(url)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    [Theory]
    [InlineData("drammers://auth")]
    [InlineData("exp://localhost:8081/--/auth")]
    [InlineData("exp://127.0.0.1:8081/--/auth")]
    [InlineData("exp://[::1]:8081/--/auth")]
    [InlineData("exp://10.0.0.5:8081/--/auth")]
    [InlineData("exps://172.16.0.1:8081/--/auth")]
    [InlineData("exp://172.31.255.255:8081/--/auth")]
    [InlineData("exp://192.168.1.10:8081/--/auth")]
    public void Eigen_app_en_lokale_expo_server_mogen(string url) => Assert.Equal(url, AppAuthController.ReturnUrl(State(url)));

    [Theory]
    [InlineData("https://evil.example.com")]
    [InlineData("exp://evil.example.com:8081/--/auth")]
    [InlineData("exps://u.expo.dev/abc/--/auth")]
    [InlineData("exp://192.168.1.10.evil.example.com/--/auth")]
    [InlineData("exp://10.0.0.5@evil.example.com/--/auth")]
    [InlineData("exp://172.32.0.1:8081/--/auth")]
    [InlineData("exp://172.15.0.1:8081/--/auth")]
    [InlineData("exp://8.8.8.8:8081/--/auth")]
    [InlineData("exp://[2001:db8::1]:8081/--/auth")]
    [InlineData("javascript:alert(1)")]
    public void Andere_adressen_niet(string url) => Assert.Null(AppAuthController.ReturnUrl(State(url)));

    [Fact]
    public void State_zonder_punt_of_geen_base64_niet()
    {
        Assert.Null(AppAuthController.ReturnUrl("zonder-punt"));
        Assert.Null(AppAuthController.ReturnUrl("abc.!!!"));
        Assert.Null(AppAuthController.ReturnUrl(null));
    }
}
