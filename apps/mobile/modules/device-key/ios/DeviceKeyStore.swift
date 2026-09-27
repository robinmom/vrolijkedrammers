import CryptoKit
import Foundation
import Security

/// Fout uit de sleutelopslag; de module maakt er een JS-fout van.
struct DeviceKeyError: Error {
  let message: String
  init(_ message: String) { self.message = message }
}

/// Sleutelopslag zonder Expo-afhankelijkheid, zodat hij los te compileren en op macOS te testen is.
enum DeviceKeyStore {
  /// DER-kop van een SubjectPublicKeyInfo voor id-ecPublicKey + prime256v1, gevolgd door het punt (04‖X‖Y).
  static let spkiHeader: [UInt8] = [
    0x30, 0x59, 0x30, 0x13, 0x06, 0x07, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x02, 0x01,
    0x06, 0x08, 0x2A, 0x86, 0x48, 0xCE, 0x3D, 0x03, 0x01, 0x07, 0x03, 0x42, 0x00,
  ]

  static func tag(_ alias: String) -> Data {
    Data("nl.vrolijkedrammers.devicekey.\(alias)".utf8)
  }

  static func generate(alias: String) throws -> [String: Any] {
    delete(alias: alias)
    let enclave = SecureEnclave.isAvailable
    var error: Unmanaged<CFError>?
    // Alleen bruikbaar als het toestel ontgrendeld is, nooit in een back-up of op een ander toestel (ADR-005).
    let flags: SecAccessControlCreateFlags = enclave ? [.privateKeyUsage] : []
    guard let access = SecAccessControlCreateWithFlags(nil, kSecAttrAccessibleWhenUnlockedThisDeviceOnly, flags, &error) else {
      throw DeviceKeyError(describe(error))
    }
    var attributes: [String: Any] = [
      kSecAttrKeyType as String: kSecAttrKeyTypeECSECPrimeRandom,
      kSecAttrKeySizeInBits as String: 256,
      kSecPrivateKeyAttrs as String: [
        kSecAttrIsPermanent as String: true,
        kSecAttrApplicationTag as String: tag(alias),
        kSecAttrAccessControl as String: access,
      ] as [String: Any],
    ]
    if enclave {
      attributes[kSecAttrTokenID as String] = kSecAttrTokenIDSecureEnclave
    }
    guard let key = SecKeyCreateRandomKey(attributes as CFDictionary, &error) else {
      throw DeviceKeyError(describe(error))
    }
    return [
      "publicKey": try publicKey(of: key).base64EncodedString(),
      "securityLevel": enclave ? "SecureEnclave" : "Software",
      "attestation": [String](),
    ]
  }

  static func find(alias: String) throws -> SecKey? {
    let query: [String: Any] = [
      kSecClass as String: kSecClassKey,
      kSecAttrKeyType as String: kSecAttrKeyTypeECSECPrimeRandom,
      kSecAttrApplicationTag as String: tag(alias),
      kSecReturnRef as String: true,
    ]
    var item: CFTypeRef?
    let status = SecItemCopyMatching(query as CFDictionary, &item)
    if status == errSecItemNotFound {
      return nil
    }
    guard status == errSecSuccess, let item else {
      throw DeviceKeyError("Sleutel lezen mislukt (\(status)).")
    }
    return (item as! SecKey)
  }

  static func publicKey(of privateKey: SecKey) throws -> Data {
    var error: Unmanaged<CFError>?
    guard let publicKey = SecKeyCopyPublicKey(privateKey),
          let point = SecKeyCopyExternalRepresentation(publicKey, &error) as Data? else {
      throw DeviceKeyError(describe(error))
    }
    return Data(spkiHeader) + point
  }

  static func sign(alias: String, data: String) throws -> String {
    guard let key = try find(alias: alias) else {
      throw DeviceKeyError("Er is geen sleutel '\(alias)'.")
    }
    guard let message = Data(base64Encoded: data) else {
      throw DeviceKeyError("Ongeldige base64-invoer.")
    }
    var error: Unmanaged<CFError>?
    guard let der = SecKeyCreateSignature(key, .ecdsaSignatureMessageX962SHA256, message as CFData, &error) as Data? else {
      throw DeviceKeyError(describe(error))
    }
    return try rawSignature(fromDer: der).base64EncodedString()
  }

  static func delete(alias: String) {
    let query: [String: Any] = [
      kSecClass as String: kSecClassKey,
      kSecAttrApplicationTag as String: tag(alias),
    ]
    SecItemDelete(query as CFDictionary)
  }

  /// ECDSA-Sig-Value (SEQUENCE { INTEGER r, INTEGER s }) naar r‖s van elk 32 bytes (ADR-005: vaste 64 bytes in de QR).
  static func rawSignature(fromDer der: Data) throws -> Data {
    let bytes = [UInt8](der)
    var index = 0
    func readLength() throws -> Int {
      guard index < bytes.count else { throw DeviceKeyError("Ongeldige handtekening.") }
      let first = Int(bytes[index]); index += 1
      if first < 0x80 { return first }
      let count = first & 0x7F
      var length = 0
      for _ in 0..<count {
        guard index < bytes.count else { throw DeviceKeyError("Ongeldige handtekening.") }
        length = (length << 8) | Int(bytes[index]); index += 1
      }
      return length
    }
    func readInteger() throws -> [UInt8] {
      guard index < bytes.count, bytes[index] == 0x02 else { throw DeviceKeyError("Ongeldige handtekening.") }
      index += 1
      let length = try readLength()
      guard index + length <= bytes.count else { throw DeviceKeyError("Ongeldige handtekening.") }
      var value = Array(bytes[index..<(index + length)]); index += length
      while value.count > 32, value.first == 0 { value.removeFirst() }
      guard value.count <= 32 else { throw DeviceKeyError("Ongeldige handtekening.") }
      return [UInt8](repeating: 0, count: 32 - value.count) + value
    }
    guard bytes.first == 0x30 else { throw DeviceKeyError("Ongeldige handtekening.") }
    index = 1
    _ = try readLength()
    return Data(try readInteger() + readInteger())
  }

  static func describe(_ error: Unmanaged<CFError>?) -> String {
    error.map { ($0.takeRetainedValue() as Error).localizedDescription } ?? "Onbekende fout in de sleutelopslag."
  }
}
