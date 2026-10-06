// Deploy-identiteit voor Productie in rg-dvd-prod-shared (blijft bestaan als rg-dvd-prod opnieuw wordt opgebouwd).
param location string
param githubRepository string
param tags object

resource deployIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2024-11-30' = {
  name: 'id-dvd-github-prod'
  location: location
  tags: tags
}

resource deployCredential 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2024-11-30' = {
  parent: deployIdentity
  name: 'github-environment-prod'
  properties: {
    issuer: 'https://token.actions.githubusercontent.com'
    subject: 'repo:${githubRepository}:environment:prod'
    audiences: ['api://AzureADTokenExchange']
  }
}

output clientId string = deployIdentity.properties.clientId
output principalId string = deployIdentity.properties.principalId
