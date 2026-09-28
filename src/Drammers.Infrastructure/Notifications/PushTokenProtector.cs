using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace Drammers.Infrastructure.Notifications;

/// <summary>
/// Push-tokens versleuteld op applicatieniveau (docs/06: ASP.NET Data Protection, sleutelring in Blob, beschermd door
/// een Key Vault-sleutel). De hash maakt tokens uniek en opzoekbaar zonder ze te ontsleutelen.
/// </summary>
public sealed class PushTokenProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector _protector = provider.CreateProtector("Drammers.PushToken.v1");

    public string Protect(string token) => _protector.Protect(token);

    public string Unprotect(string protectedToken) => _protector.Unprotect(protectedToken);

    public static string Hash(string token) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Expo-tokens: <c>ExponentPushToken[…]</c> of <c>ExpoPushToken[…]</c>.</summary>
    public static bool IsValid(string token) =>
        token.Length is > 20 and <= 200
        && (token.StartsWith("ExponentPushToken[", StringComparison.Ordinal) || token.StartsWith("ExpoPushToken[", StringComparison.Ordinal))
        && token.EndsWith(']');
}
