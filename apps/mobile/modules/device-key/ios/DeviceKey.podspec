Pod::Spec.new do |s|
  s.name           = 'DeviceKey'
  s.version        = '0.1.0'
  s.summary        = 'Hardwaresleutel (ECDSA P-256) in de Secure Enclave'
  s.description    = 'Spike OQ-68 / ADR-005: niet-exporteerbare sleutel voor de device-gebonden QR.'
  s.author         = 'De Vrolijke Drammers'
  s.homepage       = 'https://github.com/robinmom/vrolijkedrammers'
  s.platforms      = { :ios => '16.4' }
  s.source         = { git: '' }
  s.static_framework = true

  s.dependency 'ExpoModulesCore'

  s.pod_target_xcconfig = {
    'DEFINES_MODULE' => 'YES',
  }

  s.source_files = "**/*.{h,m,mm,swift,hpp,cpp}"
end
