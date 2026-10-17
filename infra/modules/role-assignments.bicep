// Rechten van de API-managed identity binnen de eigen omgeving (docs/08 §4).
param principalId string
param keyVaultName string
param storageAccountName string
param communicationServiceName string

@description('Malwarescan (Defender for Storage): de API leest de scanuitslag uit de blob index tags in quarantine.')
param readScanResults bool = false

var roles = {
  keyVaultSecretsUser: '4633458b-17de-408a-b874-0445c86b69e6'
  keyVaultCryptoServiceEncryptionUser: 'e147488a-f6f5-4113-8e2d-b22465e65bf6'
  storageBlobDataContributor: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
  storageBlobDelegator: 'db58b8e5-c6ad-4a2a-8342-4190687cbf4a'
  storageBlobDataOwner: 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
  communicationEmailServiceOwner: '09976791-48a7-449e-bb21-39d1a415f350'
}

resource vault 'Microsoft.KeyVault/vaults@2024-11-01' existing = {
  name: keyVaultName
}

resource storage 'Microsoft.Storage/storageAccounts@2025-01-01' existing = {
  name: storageAccountName
}

resource communication 'Microsoft.Communication/communicationServices@2025-09-01' existing = {
  name: communicationServiceName
}

resource vaultSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, principalId, roles.keyVaultSecretsUser)
  scope: vault
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultSecretsUser)
  }
}

// Data Protection: sleutelring wrappen/unwrappen met de sleutel 'dataprotection'.
resource vaultCryptoUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(vault.id, principalId, roles.keyVaultCryptoServiceEncryptionUser)
  scope: vault
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.keyVaultCryptoServiceEncryptionUser)
  }
}

resource blobContributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, principalId, roles.storageBlobDataContributor)
  scope: storage
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataContributor)
  }
}

resource blobDelegator 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(storage.id, principalId, roles.storageBlobDelegator)
  scope: storage
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDelegator)
  }
}

// Blob index tags lezen (blobs/tags/read) zit niet in Storage Blob Data Contributor. Ingebouwd alleen in Storage Blob
// Data Owner; daarom die rol, maar uitsluitend op de container quarantine (daar staan alleen nog niet vrijgegeven uploads).
resource quarantine 'Microsoft.Storage/storageAccounts/blobServices/containers@2025-01-01' existing = {
  name: '${storageAccountName}/default/quarantine'
}

resource quarantineOwner 'Microsoft.Authorization/roleAssignments@2022-04-01' = if (readScanResults) {
  name: guid(quarantine.id, principalId, roles.storageBlobDataOwner)
  scope: quarantine
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.storageBlobDataOwner)
  }
}

resource emailSender'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(communication.id, principalId, roles.communicationEmailServiceOwner)
  scope: communication
  properties: {
    principalId: principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.communicationEmailServiceOwner)
  }
}
