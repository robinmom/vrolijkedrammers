using System.Xml.Linq;
using Azure.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

// Versleutelt de Data Protection-sleutelring opnieuw met een andere Key Vault-sleutel (fase 7: Dev → Prod).
// De sleutels zelf blijven gelijk, zodat Prod de gegevens uit de gekopieerde database kan lezen (IBAN's, QR-sleutels,
// pushtokens, afmeldlinks). Aanmelden met de Azure CLI; de gebruiker heeft op beide sleutels "Key Vault Crypto User" nodig.
//   dotnet run --project tools/Drammers.KeyRing -- <bron-keys.xml> <bron-key-uri> <doel-key-uri> <uit-keys.xml>
if (args.Length != 4)
{
    Console.Error.WriteLine("Gebruik: Drammers.KeyRing <bron-keys.xml> <bron-key-uri> <doel-key-uri> <uit-keys.xml>");
    return 2;
}

var credential = new AzureCliCredential();
ServiceProvider Services(string keyUri)
{
    var services = new ServiceCollection();
    services.AddDataProtection().SetApplicationName("drammers").ProtectKeysWithAzureKeyVault(new Uri(keyUri), credential);
    return services.BuildServiceProvider();
}

using var source = Services(args[1]);
using var target = Services(args[2]);
var encryptor = target.GetRequiredService<IOptions<KeyManagementOptions>>().Value.XmlEncryptor
    ?? throw new InvalidOperationException("Geen Key Vault-versleuteling voor de doelsleutel.");

var document = XDocument.Load(args[0]);
var secrets = document.Descendants().Where(e => e.Name.LocalName == "encryptedSecret").ToList();
foreach (var secret in secrets)
{
    // Ontsleutelen met het decryptortype uit de sleutelring (de Key Vault-decryptor), met de bronsleutel.
    var decryptorType = Type.GetType((string)secret.Attribute("decryptorType")!, throwOnError: true)!;
    var decryptor = (IXmlDecryptor)ActivatorUtilities.CreateInstance(source, decryptorType);
    var plain = decryptor.Decrypt(secret.Elements().Single());

    var encrypted = encryptor.Encrypt(plain);
    secret.SetAttributeValue("decryptorType", encrypted.DecryptorType.AssemblyQualifiedName);
    secret.Elements().Single().ReplaceWith(encrypted.EncryptedElement);
}

document.Save(args[3]);
Console.WriteLine($"{secrets.Count} sleutel(s) opnieuw versleuteld naar {args[3]}.");
return 0;
