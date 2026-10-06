// Eenmalige bootstrap van Productie op subscription-niveau (fase 7). Uitvoeren door een beheerder met Owner-rechten
// via infra/bootstrap/bootstrap-prod.sh. Productie krijgt een eigen B1-plan in rg-dvd-prod (niet gedeeld met Dev/Acc,
// zodat Dev na de livegang naar het gratis plan kan) en een eigen deploy-identiteit die alleen werkt vanuit de GitHub
// environment "prod" (met goedkeuring).
targetScope = 'subscription'

param location string = 'swedencentral'

@description('Repository zoals GitHub die in het OIDC-subject zet: eigenaar@eigenaar-id/naam@repo-id.')
param githubRepository string

var tags = {
  application: 'de-vrolijke-drammers'
  environment: 'prod'
  managedBy: 'bicep'
}

resource sharedGroup 'Microsoft.Resources/resourceGroups@2024-11-01' = {
  name: 'rg-dvd-prod-shared'
  location: location
  tags: union(tags, { environment: 'prod-shared' })
}

resource prodGroup 'Microsoft.Resources/resourceGroups@2024-11-01' = {
  name: 'rg-dvd-prod'
  location: location
  tags: tags
}

// De what-if-identiteit en -rol bestaan al (bootstrap-nonprod); productie gebruikt ze ook voor what-if in PR's.
resource whatIfIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' existing = {
  scope: resourceGroup('rg-dvd-nonprod-shared')
  name: 'id-dvd-github-whatif'
}

module identity 'prod-identity.bicep' = {
  name: 'dvd-prod-identity'
  scope: sharedGroup
  params: {
    location: location
    githubRepository: githubRepository
    tags: union(tags, { environment: 'prod-shared' })
  }
}

module plan 'prod-plan.bicep' = {
  name: 'dvd-prod-plan'
  scope: prodGroup
  params: {
    location: location
    tags: tags
  }
}

module access 'environment-access.bicep' = {
  name: 'dvd-access-prod'
  scope: prodGroup
  params: {
    deployPrincipalId: identity.outputs.principalId
    whatIfPrincipalId: whatIfIdentity.properties.principalId
    whatIfRoleId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', guid(subscription().id, 'dvd-bicep-what-if'))
  }
}

output appServicePlanId string = plan.outputs.id
output deployClientId string = identity.outputs.clientId
output deployPrincipalId string = identity.outputs.principalId
