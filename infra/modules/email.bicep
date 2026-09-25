// Azure Communication Services Email met een Azure-managed domein; eigen domein volgt in fase 7.
param environmentName string
param tags object

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

resource communication 'Microsoft.Communication/communicationServices@2025-09-01' = {
  name: 'acs-dvd-${environmentName}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: 'Europe'
    linkedDomains: [managedDomain.id]
  }
}

output communicationServiceName string = communication.name
output endpoint string = 'https://${communication.properties.hostName}'
output senderDomain string = managedDomain.properties.mailFromSenderDomain
