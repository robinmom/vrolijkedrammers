// Beheerportal op Static Web Apps (ADR-007). Deploy via de pipeline (token wordt tijdens de run opgehaald).
param location string
param environmentName string
param tags object

@allowed(['Free', 'Standard'])
param sku string

resource portal 'Microsoft.Web/staticSites@2024-11-01' = {
  name: 'swa-dvd-admin-${environmentName}'
  location: location
  tags: tags
  sku: { name: sku, tier: sku }
  properties: {
    allowConfigFileUpdates: true
    stagingEnvironmentPolicy: 'Disabled'
  }
}

output name string = portal.name
output defaultHostName string = portal.properties.defaultHostname
