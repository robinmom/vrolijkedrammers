// Storage-account voor bestanden (ADR-008): geen anonieme toegang, geen shared key, soft delete en versioning.
param location string
param environmentName string
param tags object
param workspaceId string

@description('Blob-containers die de API gebruikt (docs/08 §3); content = afbeeldingen en bijlagen van events en nieuws.')
param containerNames array = [
  'parade-documents'
  'photos-original'
  'photos-derived'
  'quarantine'
  'exports'
  'dataprotection'
  'content'
]

@description('Microsoft Defender for Storage met malwarescan bij upload (OQ-65); kost geld, dus alleen in Prod aan.')
param malwareScanning bool = false

@description('Maximaal aantal GB dat Defender per maand scant (kostenplafond); daarboven krijgen uploads geen uitslag en worden ze niet vrijgegeven.')
param malwareScanningCapGbPerMonth int = 10

resource account 'Microsoft.Storage/storageAccounts@2025-01-01' = {
  name: 'stdvd${environmentName}'
  location: location
  tags: tags
  kind: 'StorageV2'
  sku: { name: 'Standard_LRS' }
  properties: {
    accessTier: 'Hot'
    minimumTlsVersion: 'TLS1_2'
    supportsHttpsTrafficOnly: true
    allowBlobPublicAccess: false
    allowSharedKeyAccess: false
    allowCrossTenantReplication: false
    defaultToOAuthAuthentication: true
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

resource blobService 'Microsoft.Storage/storageAccounts/blobServices@2025-01-01' = {
  parent: account
  name: 'default'
  properties: {
    isVersioningEnabled: true
    deleteRetentionPolicy: { enabled: true, days: 14 }
    containerDeleteRetentionPolicy: { enabled: true, days: 14 }
  }
}

resource containers 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = [
  for containerName in containerNames: {
    parent: blobService
    name: containerName
    properties: { publicAccess: 'None' }
  }
]

// Defender for Storage (plan DefenderForStorageV2) alleen op dit account: elke upload wordt gescand en de uitslag komt
// als blob index tag "Malware Scanning scan result" op de blob ("No threats found" / "Malicious"). De API leest die tag
// in quarantaine (DefenderMalwareScanner) en geeft alleen schone bestanden vrij. Uit = Defender op dit account uit.
resource defender 'Microsoft.Security/defenderForStorageSettings@2025-06-01' = {
  name: 'current'
  scope: account
  properties: {
    isEnabled: malwareScanning
    overrideSubscriptionLevelSettings: true
    malwareScanning: {
      onUpload: {
        isEnabled: malwareScanning
        capGBPerMonth: malwareScanningCapGbPerMonth
      }
      blobScanResultsOptions: 'BlobIndexTags'
    }
    sensitiveDataDiscovery: { isEnabled: false }
  }
}

// AVG-exports (fase 9) zijn 24 uur via de API te downloaden; daarna ruimt deze regel ze (en oude versies) op.
resource lifecycle 'Microsoft.Storage/storageAccounts/managementPolicies@2025-01-01' = {
  parent: account
  name: 'default'
  properties: {
    policy: {
      rules: [
        {
          name: 'privacy-exports-opruimen'
          enabled: true
          type: 'Lifecycle'
          definition: {
            filters: { blobTypes: ['blockBlob'], prefixMatch: ['exports/privacy/'] }
            actions: {
              baseBlob: { delete: { daysAfterModificationGreaterThan: 2 } }
              version: { delete: { daysAfterCreationGreaterThan: 2 } }
            }
          }
        }
      ]
    }
  }
}

// Nieuwste versie die categoryGroup ondersteunt.
#disable-next-line use-recent-api-versions
resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: blobService
  properties: {
    workspaceId: workspaceId
    logs: [
      { category: 'StorageWrite', enabled: true }
      { category: 'StorageDelete', enabled: true }
    ]
  }
}

output name string = account.name
output blobEndpoint string = account.properties.primaryEndpoints.blob
