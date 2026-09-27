import CryptoKit
import ExpoModulesCore

/// Spike OQ-68 (ADR-005): ECDSA P-256-sleutel in de Secure Enclave. De private sleutel verlaat de chip nooit; de app
/// krijgt alleen de publieke sleutel (SubjectPublicKeyInfo, DER) en handtekeningen (raw r‖s, 64 bytes).
public class DeviceKeyModule: Module {
  public func definition() -> ModuleDefinition {
    Name("DeviceKey")

    Function("getInfo") { () -> [String: Any] in
      ["platform": "ios", "secureHardware": SecureEnclave.isAvailable]
    }

    // De challenge is voor Android-key-attestation; iOS gebruikt daarvoor App Attest (apart, OQ-73).
    AsyncFunction("generateKeyAsync") { (alias: String, _: String?) -> [String: Any] in
      try wrap { try DeviceKeyStore.generate(alias: alias) }
    }

    AsyncFunction("getPublicKeyAsync") { (alias: String) -> String? in
      try wrap { try DeviceKeyStore.find(alias: alias).map { try DeviceKeyStore.publicKey(of: $0).base64EncodedString() } }
    }

    AsyncFunction("signAsync") { (alias: String, data: String) -> String in
      try wrap { try DeviceKeyStore.sign(alias: alias, data: data) }
    }

    AsyncFunction("deleteKeyAsync") { (alias: String) in
      DeviceKeyStore.delete(alias: alias)
    }
  }
}

final class DeviceKeyException: GenericException<String> {
  override var reason: String { param }
}

private func wrap<T>(_ body: () throws -> T) throws -> T {
  do {
    return try body()
  } catch let error as DeviceKeyError {
    throw DeviceKeyException(error.message)
  }
}
