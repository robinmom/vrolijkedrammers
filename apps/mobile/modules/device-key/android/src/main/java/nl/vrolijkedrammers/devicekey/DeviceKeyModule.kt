package nl.vrolijkedrammers.devicekey

import android.content.pm.PackageManager
import android.os.Build
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyInfo
import android.security.keystore.KeyProperties
import android.util.Base64
import expo.modules.kotlin.exception.CodedException
import expo.modules.kotlin.modules.Module
import expo.modules.kotlin.modules.ModuleDefinition
import java.security.KeyFactory
import java.security.KeyPairGenerator
import java.security.KeyStore
import java.security.PrivateKey
import java.security.ProviderException
import java.security.Signature
import java.security.spec.ECGenParameterSpec

/**
 * Spike OQ-68 (ADR-005): ECDSA P-256-sleutel in de Android Keystore, bij voorkeur in StrongBox, anders in de TEE.
 * De private sleutel verlaat de hardware nooit; de app krijgt de publieke sleutel (SubjectPublicKeyInfo, DER),
 * handtekeningen (raw r‖s, 64 bytes) en, met een challenge, de key-attestation-keten.
 */
class DeviceKeyModule : Module() {
  override fun definition() = ModuleDefinition {
    Name("DeviceKey")

    Function("getInfo") {
      val strongBox = Build.VERSION.SDK_INT >= Build.VERSION_CODES.P &&
        appContext.reactContext?.packageManager?.hasSystemFeature(PackageManager.FEATURE_STRONGBOX_KEYSTORE) == true
      mapOf("platform" to "android", "secureHardware" to strongBox)
    }

    AsyncFunction("generateKeyAsync") { alias: String, challenge: String? ->
      generate(alias, challenge?.let { Base64.decode(it, Base64.NO_WRAP) })
    }

    AsyncFunction("getPublicKeyAsync") { alias: String ->
      keyStore().getCertificate(keyAlias(alias))?.publicKey?.encoded?.let { encode(it) }
    }

    AsyncFunction("signAsync") { alias: String, data: String ->
      val key = keyStore().getKey(keyAlias(alias), null) as? PrivateKey
        ?: throw DeviceKeyException("Er is geen sleutel '$alias'.")
      val signature = Signature.getInstance("SHA256withECDSA").run {
        initSign(key)
        update(Base64.decode(data, Base64.NO_WRAP))
        sign()
      }
      encode(rawSignature(signature))
    }

    AsyncFunction("deleteKeyAsync") { alias: String ->
      keyStore().deleteEntry(keyAlias(alias))
    }
  }

  private fun generate(alias: String, challenge: ByteArray?): Map<String, Any> {
    val name = keyAlias(alias)
    keyStore().deleteEntry(name)
    val generator = KeyPairGenerator.getInstance(KeyProperties.KEY_ALGORITHM_EC, "AndroidKeyStore")
    val pair = try {
      generator.initialize(spec(name, strongBox = Build.VERSION.SDK_INT >= Build.VERSION_CODES.P, challenge))
      generator.generateKeyPair()
    } catch (e: ProviderException) {
      // Geen StrongBox (StrongBoxUnavailableException, API 28+) of die weigert: dan de TEE.
      generator.initialize(spec(name, strongBox = false, challenge))
      generator.generateKeyPair()
    }
    val attestation = if (challenge != null) {
      keyStore().getCertificateChain(name)?.map { encode(it.encoded) } ?: emptyList()
    } else {
      emptyList()
    }
    return mapOf(
      "publicKey" to encode(pair.public.encoded),
      "securityLevel" to securityLevel(pair.private),
      "attestation" to attestation,
    )
  }

  private fun spec(name: String, strongBox: Boolean, challenge: ByteArray?): KeyGenParameterSpec {
    val builder = KeyGenParameterSpec.Builder(name, KeyProperties.PURPOSE_SIGN)
      .setAlgorithmParameterSpec(ECGenParameterSpec("secp256r1"))
      .setDigests(KeyProperties.DIGEST_SHA256)
    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.P) {
      builder.setIsStrongBoxBacked(strongBox)
      // Alleen bruikbaar als het toestel ontgrendeld is (ADR-005, gestolen telefoon).
      builder.setUnlockedDeviceRequired(true)
    }
    if (challenge != null) {
      builder.setAttestationChallenge(challenge)
    }
    return builder.build()
  }

  @Suppress("DEPRECATION")
  private fun securityLevel(key: PrivateKey): String {
    val info = KeyFactory.getInstance(key.algorithm, "AndroidKeyStore").getKeySpec(key, KeyInfo::class.java)
    if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S) {
      return when (info.securityLevel) {
        KeyProperties.SECURITY_LEVEL_STRONGBOX -> "StrongBox"
        KeyProperties.SECURITY_LEVEL_TRUSTED_ENVIRONMENT -> "TrustedEnvironment"
        KeyProperties.SECURITY_LEVEL_SOFTWARE -> "Software"
        else -> "UnknownSecure"
      }
    }
    return if (info.isInsideSecureHardware) "TrustedEnvironment" else "Software"
  }

  private fun keyStore() = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }

  private fun keyAlias(alias: String) = "nl.vrolijkedrammers.devicekey.$alias"

  private fun encode(bytes: ByteArray) = Base64.encodeToString(bytes, Base64.NO_WRAP)

  companion object {
    /** ECDSA-Sig-Value (SEQUENCE { INTEGER r, INTEGER s }) naar r‖s van elk 32 bytes. */
    fun rawSignature(der: ByteArray): ByteArray {
      var index = 0
      fun readLength(): Int {
        val first = der[index++].toInt() and 0xFF
        if (first < 0x80) return first
        var length = 0
        repeat(first and 0x7F) { length = (length shl 8) or (der[index++].toInt() and 0xFF) }
        return length
      }
      fun readInteger(): ByteArray {
        if (der[index++].toInt() != 0x02) throw DeviceKeyException("Ongeldige handtekening.")
        val length = readLength()
        var value = der.copyOfRange(index, index + length)
        index += length
        while (value.size > 32 && value[0].toInt() == 0) value = value.copyOfRange(1, value.size)
        if (value.size > 32) throw DeviceKeyException("Ongeldige handtekening.")
        return ByteArray(32 - value.size) + value
      }
      if (der[index++].toInt() != 0x30) throw DeviceKeyException("Ongeldige handtekening.")
      readLength()
      return readInteger() + readInteger()
    }
  }
}

class DeviceKeyException(message: String) : CodedException(message)
