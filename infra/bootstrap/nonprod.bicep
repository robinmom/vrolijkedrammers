// Eenmalige bootstrap van de non-prod-omgevingen (Dev + Acc) op subscription-niveau.
// Uitvoeren door een beheerder met Owner-rechten via infra/bootstrap/bootstrap-nonprod.sh.
targetScope = 'subscription'

param location string = 'swedencentral'

@description('Repository zoals GitHub die in het OIDC-subject zet: eigenaar@eigenaar-id/naam@repo-id (nieuwe, onveranderlijke vorm).')
param githubRepository string

param environments array = ['dev', 'acc']

@description('Maandbudget voor de gedeelde resources (plan).')
param sharedBudgetAmount int = 20
param budgetStartDate string = '2026-09-01'
param budgetContactEmails array = []

var tags = {
  application: 'de-vrolijke-drammers'
  environment: 'nonprod-shared'
  managedBy: 'bicep'
}

resource sharedGroup 'Microsoft.Resources/resourceGroups@2024-11-01' = {
  name: 'rg-dvd-nonprod-shared'
  location: location
  tags: tags
}

resource environmentGroups 'Microsoft.Resources/resourceGroups@2024-11-01' = [
  for env in environments: {
    name: 'rg-dvd-${env}'
    location: location
    tags: union(tags, { environment: env })
  }
]

// Alleen-lezen rol voor what-if in pull requests.
resource whatIfRole 'Microsoft.Authorization/roleDefinitions@2022-04-01' = {
  name: guid(subscription().id, 'dvd-bicep-what-if')
  properties: {
    roleName: 'DVD Bicep What-If'
    description: 'Lezen en what-if/validate van deployments; kan niets wijzigen.'
    type: 'CustomRole'
    assignableScopes: [subscription().id]
    permissions: [
      {
        actions: [
          '*/read'
          'Microsoft.Resources/deployments/validate/action'
          'Microsoft.Resources/deployments/whatIf/action'
        ]
      }
    ]
  }
}

module shared 'shared.bicep' = {
  name: 'dvd-nonprod-shared'
  scope: sharedGroup
  params: {
    location: location
    githubRepository: githubRepository
    environments: environments
    tags: tags
  }
}

module access 'environment-access.bicep' = [
  for (env, i) in environments: {
    name: 'dvd-access-${env}'
    scope: environmentGroups[i]
    params: {
      deployPrincipalId: shared.outputs.deployIdentities[i].principalId
      whatIfPrincipalId: shared.outputs.whatIfPrincipalId
      whatIfRoleId: whatIfRole.id
    }
  }
]

module sharedBudget '../modules/budget.bicep' = if (!empty(budgetContactEmails)) {
  name: 'dvd-budget-nonprod-shared'
  scope: sharedGroup
  params: {
    environmentName: 'nonprod-shared'
    amount: sharedBudgetAmount
    startDate: budgetStartDate
    contactEmails: budgetContactEmails
  }
}

output appServicePlanId string = shared.outputs.appServicePlanId
output deployIdentities array = shared.outputs.deployIdentities
output whatIfClientId string = shared.outputs.whatIfClientId
