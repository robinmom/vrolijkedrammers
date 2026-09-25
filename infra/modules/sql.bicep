// Azure SQL met alleen Entra-authenticatie (docs/08 §5, OQ-60). Geen SQL-logins en geen "Allow Azure services".
param location string
param environmentName string
param tags object

@description('Entra-groep die SQL-beheerder is (beheerders + pipeline-identiteit).')
param adminGroupName string
param adminGroupObjectId string

@description('Gebruik het gratis Azure SQL-aanbod (serverless, pauzeert bij opgebruikte limiet).')
param useFreeOffer bool

resource server 'Microsoft.Sql/servers@2025-01-01' = {
  name: 'sql-dvd-${environmentName}'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: 'Enabled'
    restrictOutboundNetworkAccess: 'Disabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      principalType: 'Group'
      login: adminGroupName
      sid: adminGroupObjectId
      tenantId: tenant().tenantId
    }
  }
}

resource database 'Microsoft.Sql/servers/databases@2025-01-01' = {
  parent: server
  name: 'sqldb-dvd'
  location: location
  tags: tags
  sku: {
    name: 'GP_S_Gen5'
    tier: 'GeneralPurpose'
    family: 'Gen5'
    capacity: 2
  }
  properties: {
    useFreeLimit: useFreeOffer
    freeLimitExhaustionBehavior: useFreeOffer ? 'AutoPause' : null
    autoPauseDelay: 60
    minCapacity: json('0.5')
    requestedBackupStorageRedundancy: 'Local'
    zoneRedundant: false
  }
}

output serverName string = server.name
output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
