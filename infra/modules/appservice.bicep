// API + hosted workers op App Service Linux (ADR-007, B-01). Plan wordt gedeeld (Dev/Acc) en bestaat al.
param location string
param environmentName string
param tags object
param workspaceId string

@description('Resource-ID van het (gedeelde) App Service-plan.')
param appServicePlanId string

@description('App settings (geen secrets; secrets via Key Vault-references).')
param appSettings object

@description('Always On (niet beschikbaar op het gratis F1-plan).')
param alwaysOn bool = true

@description('Eigen domeinen (bijv. www.vrolijkedrammers.nl en vrolijkedrammers.nl). Pas invullen als de DNS-records staan: CNAME of A plus TXT asuid.<domein> met customDomainVerificationId.')
param customHostNames array = []

@description('Eigen domeinen die al met SNI (certificaat) aan de app hangen; die krijgen geen nieuwe binding zonder certificaat, anders staat HTTPS tijdens elke uitrol even uit. De deploy-workflow vult dit.')
param boundHostNames array = []

var newHostNames = filter(customHostNames, host => !contains(boundHostNames, host))

resource api 'Microsoft.Web/sites@2024-11-01' = {
  name: 'app-dvd-api-${environmentName}'
  location: location
  tags: tags
  kind: 'app,linux'
  identity: { type: 'SystemAssigned' }
  properties: {
    serverFarmId: appServicePlanId
    httpsOnly: true
    clientAffinityEnabled: false
    publicNetworkAccess: 'Enabled'
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      alwaysOn: alwaysOn
      http20Enabled: true
      minTlsVersion: '1.2'
      scmMinTlsVersion: '1.2'
      ftpsState: 'Disabled'
      remoteDebuggingEnabled: false
      healthCheckPath: '/health/live'
      appSettings: [for setting in items(appSettings): { name: setting.key, value: setting.value }]
    }
  }
}

// Geen publicatie met gebruikersnaam/wachtwoord (FTP en SCM basic auth uit); deploy via Entra/OIDC.
resource ftpPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-11-01' = {
  parent: api
  name: 'ftp'
  properties: { allow: false }
}

resource scmPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2024-11-01' = {
  parent: api
  name: 'scm'
  properties: { allow: false }
}

// Nieuwste versie die categoryGroup ondersteunt.
#disable-next-line use-recent-api-versions
resource diagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'to-log-analytics'
  scope: api
  properties: {
    workspaceId: workspaceId
    logs: [
      { category: 'AppServiceHTTPLogs', enabled: true }
      { category: 'AppServiceConsoleLogs', enabled: true }
      { category: 'AppServiceAppLogs', enabled: true }
      { category: 'AppServiceAuditLogs', enabled: true }
    ]
  }
}

// Eigen domeinen: eerst de binding zonder certificaat (alleen voor nieuwe domeinen), dan een gratis beheerd certificaat,
// dan SNI met dat certificaat. Strikt na elkaar: App Service weigert een wijziging aan de site terwijl een andere nog
// loopt (409 "another operation is in progress"), ook van de publicatie- en diagnose-instellingen hierboven.
@batchSize(1)
resource hostNames 'Microsoft.Web/sites/hostNameBindings@2024-11-01' = [
  for host in newHostNames: {
    parent: api
    name: host
    properties: {
      siteName: api.name
      hostNameType: 'Verified'
      sslState: 'Disabled'
      customHostNameDnsRecordType: length(split(host, '.')) > 2 ? 'CName' : 'A'
    }
    dependsOn: [ftpPolicy, scmPolicy, diagnostics]
  }
]

@batchSize(1)
resource certificates 'Microsoft.Web/certificates@2024-11-01' = [
  for host in customHostNames: {
    name: '${host}-${api.name}'
    location: location
    tags: tags
    properties: {
      serverFarmId: appServicePlanId
      canonicalName: host
    }
    dependsOn: [hostNames]
  }
]

@batchSize(1)
module sni 'hostname-sni.bicep' = [
  for (host, i) in customHostNames: {
    name: 'sni-${replace(host, '.', '-')}'
    params: {
      siteName: api.name
      hostName: host
      thumbprint: certificates[i].properties.thumbprint
    }
    dependsOn: [hostNames, ftpPolicy, scmPolicy, diagnostics]
  }
]

output name string = api.name
output defaultHostName string = api.properties.defaultHostName
output customDomainVerificationId string = api.properties.customDomainVerificationId
output principalId string = api.identity.principalId
output possibleOutboundIpAddresses array = split(api.properties.possibleOutboundIpAddresses, ',')
