// Storage-account voor bestanden (ADR-008): geen anonieme toegang, geen shared key, soft delete en versioning.
param location string
param environmentName string
param tags object
param workspaceId string

// Herstel (docs/08 §8, 2026-10-17): soft delete en, in Prod, de hele opslag terug naar een tijdstip.
@description('Verwijderde bestanden en containers zoveel dagen terug te halen.')
@minValue(1)
@maxValue(365)
param softDeleteDays int = 14

@description('Alle block blobs terug naar een tijdstip in de laatste N dagen (0 = uit); moet kleiner zijn dan softDeleteDays.')
@minValue(0)
@maxValue(364)
param restoreDays int = 0

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
    deleteRetentionPolicy: { enabled: true, days: softDeleteDays }
    containerDeleteRetentionPolicy: { enabled: true, days: softDeleteDays }
    // Point-in-time restore heeft de change feed en versioning nodig.
    changeFeed: { enabled: restoreDays > 0 }
    restorePolicy: restoreDays > 0 ? { enabled: true, days: max(restoreDays, 1) } : { enabled: false }
  }
}

resource containers 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' = [
  for containerName in containerNames: {
    parent: blobService
    name: containerName
    properties: { publicAccess: 'None' }
  }
]

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
