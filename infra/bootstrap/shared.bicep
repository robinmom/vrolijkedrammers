// Gedeelde non-prod-resources in rg-dvd-nonprod-shared: het B1-plan (Dev + Acc, ADR-007) en de
// GitHub-pipeline-identiteiten met OIDC-federatie (geen secrets). Staat los van rg-dvd-dev/-acc,
// zodat een omgeving verwijderd en opnieuw opgebouwd kan worden zonder de pipeline kwijt te raken.
param location string
param githubRepository string
param tags object

@description('Omgevingen met een eigen deploy-identiteit (subject: GitHub environment).')
param environments array

resource plan 'Microsoft.Web/serverfarms@2024-11-01' = {
  name: 'asp-dvd-nonprod'
  location: location
  tags: tags
  kind: 'linux'
  sku: { name: 'B1', tier: 'Basic', capacity: 1 }
  properties: { reserved: true }
}

resource deployIdentities 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = [
  for env in environments: {
    name: 'id-dvd-github-${env}'
    location: location
    tags: tags
  }
]

@batchSize(1) // federatieve credentials op dezelfde identiteit mogen niet parallel worden aangemaakt
resource deployCredentials 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = [
  for (env, i) in environments: {
    parent: deployIdentities[i]
    name: 'github-environment-${env}'
    properties: {
      issuer: 'https://token.actions.githubusercontent.com'
      subject: 'repo:${githubRepository}:environment:${env}'
      audiences: ['api://AzureADTokenExchange']
    }
  }
]

// Alleen-lezen identiteit voor `what-if` in pull requests (kan niets wijzigen).
resource whatIfIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-dvd-github-whatif'
  location: location
  tags: tags
}

resource whatIfCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  parent: whatIfIdentity
  name: 'github-pull-request'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${githubRepository}:pull_request'
    audiences: ['api://AzureADTokenExchange']
  }
}

// Website Contributor op het plan: nodig om een app aan het gedeelde plan te koppelen (serverFarms/join).
var websiteContributor = 'de139f84-1756-47ae-9be6-808fbbe84772'

resource planJoin 'Microsoft.Authorization/roleAssignments@2022-04-01' = [
  for (env, i) in environments: {
    name: guid(plan.id, env, websiteContributor)
    scope: plan
    properties: {
      principalId: deployIdentities[i].properties.principalId
      principalType: 'ServicePrincipal'
      roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', websiteContributor)
    }
  }
]

output appServicePlanId string = plan.id
output deployIdentities array = [
  for (env, i) in environments: {
    environment: env
    clientId: deployIdentities[i].properties.clientId
    principalId: deployIdentities[i].properties.principalId
  }
]
output whatIfClientId string = whatIfIdentity.properties.clientId
output whatIfPrincipalId string = whatIfIdentity.properties.principalId
