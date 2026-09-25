using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Drammers.Infrastructure.Auditing;

/// <summary>
/// Maskeert gevoelige velden in de oude/nieuwe waarden van een auditregel voordat ze in het portal getoond worden
/// (fase 4-security): contactgegevens, geboortedatum, bankgegevens, tokens en wachtwoorden.
/// </summary>
public static partial class AuditMasking
{
    public const string Mask = "***";

    public static string? Apply(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return json;
        }

        try
        {
            var node = JsonNode.Parse(json);
            MaskNode(node);
            return node?.ToJsonString();
        }
        catch (System.Text.Json.JsonException)
        {
            // Geen geldige JSON: niets tonen dat gevoelig zou kunnen zijn.
            return Mask;
        }
    }

    private static void MaskNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(p => p.Key).ToList())
                {
                    if (SensitiveKey().IsMatch(key))
                    {
                        obj[key] = Mask;
                    }
                    else
                    {
                        MaskNode(obj[key]);
                    }
                }

                break;
            case JsonArray array:
                foreach (var item in array)
                {
                    MaskNode(item);
                }

                break;
        }
    }

    [GeneratedRegex("mail|phone|telefoon|address|adres|iban|birth|geboorte|token|password|wachtwoord|secret", RegexOptions.IgnoreCase)]
    private static partial Regex SensitiveKey();
}
