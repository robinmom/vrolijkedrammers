// Eigen App Service-plan voor Productie (B1, Linux). In dezelfde resource group als de app, zodat de gratis beheerde
// certificaten voor het eigen domein bij de app kunnen worden aangemaakt.
param location string
param tags object

resource plan 'Microsoft.Web/serverfarms@2024-11-01' = {
  name: 'asp-dvd-prod'
  location: location
  tags: tags
  kind: 'linux'
  sku: { name: 'B1', tier: 'Basic', capacity: 1 }
  properties: { reserved: true }
}

output id string = plan.id
