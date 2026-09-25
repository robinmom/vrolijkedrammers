// Orchestrator per omgeving (docs/08 §6). Scope: de resource group van de omgeving (rg-dvd-<env>).
// Voorwaarde: de bootstrap (infra/bootstrap) heeft de resource groups, het gedeelde plan en de SQL-beheergroep aangemaakt.
targetScope = 'resourceGroup'

@allowed(['dev', 'acc', 'prod'])
param environmentName string
param location string = resourceGroup().location

@description('Resource-ID van het App Service-plan (Dev/Acc delen één B1-plan).')
param appServicePlanId string

@description('Entra-groep (tenant van de subscription) die SQL-beheerder is.')
param sqlAdminGroupName string
param sqlAdminGroupObjectId string
param sqlUseFreeOffer bool

param keyVaultPurgeProtection bool
param logDailyQuotaGb int

@description('Entra External ID: authority, client-ID van de API-app-registratie en de vereiste environmentAccess-waarde (leeg in Prod).')
param externalIdAuthority string
param apiClientId string
param environmentAccessClaim string
param requiredEnvironmentAccess string

@description('Graph in de External ID-tenant: provisioning-app, issuer-domein en het certificaat in Key Vault (provisioning-certificate.sh).')
param externalIdTenantId string
param graphClientId string
param externalIdIssuerDomain string
param graphCertificateName string

param budgetAmount int
param budgetStartDate string
param budgetContactEmails array

var tags = {
  application: 'de-vrolijke-drammers'
  environment: environmentName
  managedBy: 'bicep'
}

var aspnetEnvironment = {
  dev: 'Dev'
  acc: 'Acc'
  prod: 'Production'
}

module monitoring 'modules/monitoring.bicep' = {
  name: 'monitoring'
  params: {
    location: location
    environmentName: environmentName
    tags: tags
    dailyQuotaGb: logDailyQuotaGb
  }
}

module keyVault 'modules/keyvault.bicep' = {
  name: 'keyvault'
  params: {
    location: location
    environmentName: environmentName
    tags: tags
    workspaceId: monitoring.outputs.workspaceId
    enablePurgeProtection: keyVaultPurgeProtection
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  params: {
    location: location
    environmentName: environmentName
    tags: tags
    workspaceId: monitoring.outputs.workspaceId
  }
}

module sql 'modules/sql.bicep' = {
  name: 'sql'
  params: {
    location: location
    environmentName: environmentName
    tags: tags
    adminGroupName: sqlAdminGroupName
    adminGroupObjectId: sqlAdminGroupObjectId
    useFreeOffer: sqlUseFreeOffer
  }
}

module email 'modules/email.bicep' = {
  name: 'email'
  params: {
    environmentName: environmentName
    tags: tags
  }
}

module api 'modules/appservice.bicep' = {
  name: 'api'
  params: {
    location: location
    environmentName: environmentName
    tags: tags
    workspaceId: monitoring.outputs.workspaceId
    appServicePlanId: appServicePlanId
    appSettings: {
      ASPNETCORE_ENVIRONMENT: aspnetEnvironment[environmentName]
      // App draait uit het zip-pakket, dat bij een deploy in één keer wordt gewisseld (geen half vervangen DLL's).
      WEBSITE_RUN_FROM_PACKAGE: '1'
      APPLICATIONINSIGHTS_CONNECTION_STRING: monitoring.outputs.appInsightsConnectionString
      ConnectionStrings__Drammers: 'Server=tcp:${sql.outputs.serverFqdn},1433;Database=${sql.outputs.databaseName};Authentication=Active Directory Managed Identity;Encrypt=True;TrustServerCertificate=False;Connect Timeout=60'
      Azure__KeyVaultUri: keyVault.outputs.uri
      Azure__BlobEndpoint: storage.outputs.blobEndpoint
      Email__Endpoint: email.outputs.endpoint
      Email__SenderDomain: email.outputs.senderDomain
      Auth__Authority: externalIdAuthority
      Auth__Audience: apiClientId
      Auth__EnvironmentAccessClaim: environmentAccessClaim
      Auth__RequiredEnvironmentAccess: requiredEnvironmentAccess
      Graph__TenantId: empty(graphClientId) ? '' : externalIdTenantId
      Graph__ClientId: graphClientId
      Graph__IssuerDomain: externalIdIssuerDomain
      Graph__CertificateName: graphCertificateName
    }
  }
}

// Client-ID van de API-identiteit; nodig om de databasegebruiker aan te maken (tools/Drammers.DbSetup).
module apiIdentity 'modules/site-identity.bicep' = {
  name: 'api-identity'
  params: {
    siteName: api.outputs.name
  }
}

module sqlFirewall 'modules/sql-firewall.bicep' = {
  name: 'sql-firewall'
  params: {
    sqlServerName: sql.outputs.serverName
    ipAddresses: api.outputs.possibleOutboundIpAddresses
  }
}

module apiRoles 'modules/role-assignments.bicep' = {
  name: 'api-roles'
  params: {
    principalId: api.outputs.principalId
    keyVaultName: keyVault.outputs.name
    storageAccountName: storage.outputs.name
    communicationServiceName: email.outputs.communicationServiceName
  }
}


module budget 'modules/budget.bicep' = if (!empty(budgetContactEmails)) {
  name: 'budget'
  params: {
    environmentName: environmentName
    amount: budgetAmount
    startDate: budgetStartDate
    contactEmails: budgetContactEmails
  }
}

output apiAppName string = api.outputs.name
output apiIdentityClientId string = apiIdentity.outputs.clientId
output apiUrl string = 'https://${api.outputs.defaultHostName}'
// Het beheerportal wordt door de API-app geserveerd (OQ-76: alles in de EU).
output portalUrl string = 'https://${api.outputs.defaultHostName}/beheer/'
output sqlServerFqdn string = sql.outputs.serverFqdn
output keyVaultName string = keyVault.outputs.name
output storageAccountName string = storage.outputs.name
