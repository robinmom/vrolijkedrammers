// Azure Communication Services Email: altijd het Azure-managed domein (DoNotReply@…azurecomm.net, max. ~10 per uur),
// in Productie daarnaast het eigen domein vrolijkedrammers.nl (runbook docs/runbooks/eigen-maildomein.md). Twee stappen:
//   1. customDomain gezet: het domein wordt aangemaakt; de DNS-records voor verificatie staan in de output;
//   2. na de DNS-records en de verificatie (door de beheerder): customDomainVerified = true koppelt het domein aan ACS.
// Een niet-geverifieerd domein kan niet worden gekoppeld; daarom de tweede stap.
param environmentName string
param tags object

@description('Eigen afzenderdomein, bijv. vrolijkedrammers.nl; leeg = alleen het Azure-domein.')
param customDomain string = ''

@description('Zet pas op true als het eigen domein in ACS is geverifieerd (Domain, SPF, DKIM, DKIM2).')
param customDomainVerified bool = false

@description('Afzenders op het eigen domein (deel vóór de @) met hun weergavenaam.')
param senders object = {}

resource emailService 'Microsoft.Communication/emailServices@2025-09-01' = {
  name: 'ecs-dvd-${environmentName}'
  location: 'global'
  tags: tags
  properties: { dataLocation: 'Europe' }
}

resource managedDomain 'Microsoft.Communication/emailServices/domains@2025-09-01' = {
  parent: emailService
  name: 'AzureManagedDomain'
  location: 'global'
  tags: tags
  properties: {
    domainManagement: 'AzureManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource ownDomain 'Microsoft.Communication/emailServices/domains@2025-09-01' = if (!empty(customDomain)) {
  parent: emailService
  name: empty(customDomain) ? 'geen' : customDomain
  location: 'global'
  tags: tags
  properties: {
    domainManagement: 'CustomerManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource ownSenders 'Microsoft.Communication/emailServices/domains/senderUsernames@2025-09-01' = [
  for sender in items(senders): if (!empty(customDomain)) {
    parent: ownDomain
    name: sender.key
    properties: {
      username: sender.key
      displayName: sender.value
    }
  }
]

resource communication 'Microsoft.Communication/communicationServices@2025-09-01' = {
  name: 'acs-dvd-${environmentName}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: 'Europe'
    linkedDomains: !empty(customDomain) && customDomainVerified ? [managedDomain.id, ownDomain.id] : [managedDomain.id]
  }
}

output communicationServiceName string = communication.name
output endpoint string = 'https://${communication.properties.hostName}'
output senderDomain string = managedDomain.properties.mailFromSenderDomain
@description('Het eigen domein zodra het gekoppeld is (anders leeg); de app gebruikt dan de eigen afzenders.')
output customSenderDomain string = !empty(customDomain) && customDomainVerified ? customDomain : ''
@description('DNS-records die het eigen domein nodig heeft (Domain, SPF, DKIM, DKIM2, DMARC).')
output customDomainRecords object = !empty(customDomain) ? ownDomain!.properties.verificationRecords : {}
