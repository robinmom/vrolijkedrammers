// Azure Communication Services Email: altijd het Azure-managed domein (DoNotReply@…azurecomm.net, max. ~10 per uur),
// in Productie daarnaast het eigen domein vrolijkedrammers.nl (runbook docs/runbooks/eigen-maildomein.md). Twee stappen:
//   1. customDomain gezet: het domein wordt aangemaakt; de DNS-records voor verificatie staan in de output;
//   2. na de DNS-records en de verificatie (door de beheerder): customDomainVerified = true koppelt het domein aan ACS
//      en laat de domeinresource voortaan ongemoeid, want een CreateOrUpdate wist de verificatie (zie bij ownDomain).
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

// Het domein wordt alleen aangemaakt zolang het nog niet geverifieerd is. Daarna blijft het met rust: elke
// CreateOrUpdate op deze resource zet de verificatie van Domain, SPF, DKIM en DKIM2 terug op NotStarted, en dan
// ondertekent ACS de mail niet meer. Met een DMARC op p=quarantine verdwijnt die mail bij de ontvanger zonder dat ACS
// of wij iets zien: ACS antwoordt gewoon 202. Op 12-11-2026 kwam daardoor geen enkele aanmeldcode meer aan, en de
// handmatige reparatie van 11-11 bleek door de deploy van die dag alweer ongedaan gemaakt. Herstellen gaat met
// initiateVerification; zie docs/runbooks/eigen-maildomein.md.
resource ownDomain 'Microsoft.Communication/emailServices/domains@2025-09-01' = if (!empty(customDomain) && !customDomainVerified) {
  parent: emailService
  name: empty(customDomain) ? 'geen' : customDomain
  location: 'global'
  tags: tags
  properties: {
    domainManagement: 'CustomerManaged'
    userEngagementTracking: 'Disabled'
  }
}

resource verifiedDomain 'Microsoft.Communication/emailServices/domains@2025-09-01' existing = if (!empty(customDomain) && customDomainVerified) {
  parent: emailService
  name: empty(customDomain) ? 'geen' : customDomain
}

var ownDomainId = '${emailService.id}/domains/${customDomain}'

// De afzenders worden met hun volledige naam opgegeven, omdat het bovenliggende domein uit twee verklaringen kan
// komen. Toevoegen van een afzender blijft zo gewoon werken, ook nadat het domein geverifieerd is.
resource ownSenders 'Microsoft.Communication/emailServices/domains/senderUsernames@2025-09-01' = [
  for sender in items(senders): if (!empty(customDomain)) {
    name: '${emailService.name}/${customDomain}/${sender.key}'
    properties: {
      username: sender.key
      displayName: sender.value
    }
    dependsOn: [ownDomain]
  }
]

resource communication 'Microsoft.Communication/communicationServices@2025-09-01' = {
  name: 'acs-dvd-${environmentName}'
  location: 'global'
  tags: tags
  properties: {
    dataLocation: 'Europe'
    linkedDomains: !empty(customDomain) && customDomainVerified ? [managedDomain.id, ownDomainId] : [managedDomain.id]
  }
}

output communicationServiceName string = communication.name
output endpoint string = 'https://${communication.properties.hostName}'
output senderDomain string = managedDomain.properties.mailFromSenderDomain
@description('Het eigen domein zodra het gekoppeld is (anders leeg); de app gebruikt dan de eigen afzenders.')
output customSenderDomain string = !empty(customDomain) && customDomainVerified ? customDomain : ''
@description('DNS-records die het eigen domein nodig heeft (Domain, SPF, DKIM, DKIM2, DMARC).')
output customDomainRecords object = empty(customDomain)
  ? {}
  : (customDomainVerified ? verifiedDomain!.properties.verificationRecords : ownDomain!.properties.verificationRecords)
