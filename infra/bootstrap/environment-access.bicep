// Rechten van de pipeline op één omgeving (docs/08 §4): Contributor op de eigen resource group en
// roltoewijzingen uitsluitend voor de rollen die main.bicep aan de API-identiteit geeft.
param deployPrincipalId string
param whatIfPrincipalId string
param whatIfRoleId string

var roles = {
  contributor: 'b24988ac-6180-42a0-ab88-20f7382dd24c'
  rbacAdministrator: 'f58310d9-a9f6-439a-9e8d-f62e7b41a168'
}

// De rollen die main.bicep (modules/role-assignments.bicep) toekent.
var delegatableRoles = [
  '4633458b-17de-408a-b874-0445c86b69e6' // Key Vault Secrets User
  'ba92f5b4-2d11-453d-a403-e96b0029c9fe' // Storage Blob Data Contributor
  'db58b8e5-c6ad-4a2a-8342-4190687cbf4a' // Storage Blob Delegator
  '09976791-48a7-449e-bb21-39d1a415f350' // Communication and Email Service Owner
]
var roleList = join(delegatableRoles, ', ')
var rbacCondition = '((!(ActionMatches{\'Microsoft.Authorization/roleAssignments/write\'})) OR (@Request[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {${roleList}})) AND ((!(ActionMatches{\'Microsoft.Authorization/roleAssignments/delete\'})) OR (@Resource[Microsoft.Authorization/roleAssignments:RoleDefinitionId] ForAnyOfAnyValues:GuidEquals {${roleList}}))'

resource contributor 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, deployPrincipalId, roles.contributor)
  properties: {
    principalId: deployPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.contributor)
  }
}

resource constrainedRbacAdmin 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, deployPrincipalId, roles.rbacAdministrator)
  properties: {
    principalId: deployPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', roles.rbacAdministrator)
    conditionVersion: '2.0'
    condition: rbacCondition
    description: 'Pipeline mag alleen de rollen van de API-identiteit toewijzen'
  }
}

resource whatIf 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, whatIfPrincipalId, whatIfRoleId)
  properties: {
    principalId: whatIfPrincipalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: whatIfRoleId
  }
}
