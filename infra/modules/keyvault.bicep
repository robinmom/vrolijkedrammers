// Key Vault met RBAC-autorisatie (docs/06, docs/08 §4). Secrets worden buiten Bicep gezet (runbook).
param location string
param environmentName string
param tags object
param workspaceId string

@description('Purge protection kan na aanzetten niet meer uit; in Dev uit zodat de omgeving na verwijderen opnieuw kan worden opgebouwd.')
param enablePurgeProtection bool

resource vault 'Microsoft.KeyVault/vaults@2024-11-01' = {
  name: 'kv-dvd-${environmentName}'
  location: location
  tags: tags
  properties: {
    tenantId: tenant().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    // De eigenschap mag alleen true zijn of ontbreken.
    enablePurgeProtection: enablePurgeProtection ? true : null
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      bypass: 'AzureServices'
      defaultAction: 'Allow'
    }
  }
}

// Nieuwste versie die categoryGroup ondersteunt.
#disable-next-line use-recent-api-versions
resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: vault
  properties: {
    workspaceId: workspaceId
    logs: [{ categoryGroup: 'audit', enabled: true }]
  }
}

output name string = vault.name
output uri string = vault.properties.vaultUri
