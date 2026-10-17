// Azure SQL met alleen Entra-authenticatie (docs/08 §5, OQ-60). Geen SQL-logins en geen "Allow Azure services".
param location string
param environmentName string
param tags object

@description('Entra-groep die SQL-beheerder is (beheerders + pipeline-identiteit).')
param adminGroupName string
param adminGroupObjectId string

@description('Gebruik het gratis Azure SQL-aanbod (serverless, pauzeert bij opgebruikte limiet).')
param useFreeOffer bool

@description('Gratis tegoed op: AutoPause = pauzeren tot de volgende maand; BillOverUsage = doorgaan tegen betaling (de database pauzeert bij rust nog steeds).')
@allowed(['AutoPause', 'BillOverUsage'])
param freeLimitExhaustionBehavior string = 'AutoPause'

// 2026-10-17: GP_S_Gen5 = serverless (betalen per seconde dat hij wakker is); Basic/S0/S1/S2 = vaste prijs per maand.
// Prod is door de achtergrondtaken altijd wakker, dus daar is een vaste grootte veel goedkoper.
@description('Grootte van de database: GP_S_Gen5 (serverless) of een vaste grootte (Basic, S0, S1, S2).')
@allowed(['GP_S_Gen5', 'Basic', 'S0', 'S1', 'S2'])
param skuName string = 'GP_S_Gen5'

var serverless = skuName == 'GP_S_Gen5'

// Back-ups (docs/08 §8, 2026-10-17).
@description('Terugzetten naar elk moment in de laatste N dagen (point-in-time restore); Basic kan hooguit 7.')
@minValue(1)
@maxValue(35)
param pitrDays int = 7

@description('Langetermijnback-ups: elke maand één, zoveel maanden bewaren (0 = uit). Blijven ook bestaan als de server wordt verwijderd.')
@minValue(0)
@maxValue(120)
param ltrMonths int = 0

@description('Back-ups ook in een tweede regio (geo-redundant), voor terugzetten bij uitval van de regio.')
param geoBackup bool = false

var backupRedundancy = geoBackup ? 'Geo' : 'Local'

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
  sku: serverless
    ? {
        name: 'GP_S_Gen5'
        tier: 'GeneralPurpose'
        family: 'Gen5'
        capacity: 2
      }
    : {
        name: skuName
        tier: skuName == 'Basic' ? 'Basic' : 'Standard'
      }
  properties: serverless
    ? {
        useFreeLimit: useFreeOffer
        freeLimitExhaustionBehavior: useFreeOffer ? freeLimitExhaustionBehavior : null
        autoPauseDelay: 60
        minCapacity: json('0.5')
        requestedBackupStorageRedundancy: backupRedundancy
        zoneRedundant: false
      }
    : {
        // Basic kan hooguit 2 GB; Standard houdt de huidige 32 GB. Het gratis aanbod geldt alleen voor serverless.
        useFreeLimit: false
        maxSizeBytes: skuName == 'Basic' ? 2147483648 : 34359738368
        requestedBackupStorageRedundancy: backupRedundancy
        zoneRedundant: false
      }
}

resource shortTerm 'Microsoft.Sql/servers/databases/backupShortTermRetentionPolicies@2025-01-01' = {
  parent: database
  name: 'default'
  properties: {
    retentionDays: pitrDays
    diffBackupIntervalInHours: 12
  }
}

resource longTerm 'Microsoft.Sql/servers/databases/backupLongTermRetentionPolicies@2025-01-01' = if (ltrMonths > 0) {
  parent: database
  name: 'default'
  properties: {
    // Wekelijks 4 weken en maandelijks een jaar (de eerste back-up van de week/maand).
    weeklyRetention: 'P4W'
    monthlyRetention: 'P${ltrMonths}M'
    yearlyRetention: 'PT0S'
    weekOfYear: 1
  }
}

output serverName string = server.name
output serverFqdn string = server.properties.fullyQualifiedDomainName
output databaseName string = database.name
