// Client-ID van de system-assigned identity van een App Service. Losse module: de parameter hangt af van de
// output van de app-module, zodat de lookup pas gebeurt nadat de app bestaat (bij een nieuwe omgeving).
param siteName string

resource site 'Microsoft.Web/sites@2024-11-01' existing = {
  name: siteName
}

resource identity 'Microsoft.ManagedIdentity/identities@2024-11-30' existing = {
  scope: site
  name: 'default'
}

output clientId string = identity.properties.clientId
