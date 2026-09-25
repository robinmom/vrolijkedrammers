// API + hosted workers op App Service Linux (ADR-007, B-01). Plan wordt gedeeld (Dev/Acc) en bestaat al.
param location string
param environmentName string
param tags object
param workspaceId string

@description('Resource-ID van het (gedeelde) App Service-plan.')
param appServicePlanId string

@description('App settings (geen secrets; secrets via Key Vault-references).')
param appSettings object

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
      alwaysOn: true
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

// Client-ID van de system-assigned identity; nodig om de databasegebruiker aan te maken (tools/Drammers.DbSetup).
resource apiIdentity 'Microsoft.ManagedIdentity/identities@2024-11-30' existing = {
  scope: api
  name: 'default'
}

output name string = api.name
output clientId string = apiIdentity.properties.clientId
output defaultHostName string = api.properties.defaultHostName
output principalId string = api.identity.principalId
output possibleOutboundIpAddresses array = split(api.properties.possibleOutboundIpAddresses, ',')
