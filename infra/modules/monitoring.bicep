// Log Analytics + workspace-based Application Insights (docs/08 §9).
param location string
param environmentName string
param tags object

@description('Dagelijkse ingest-limiet in GB; houdt de kosten van Dev/Acc begrensd.')
param dailyQuotaGb int

resource workspace 'Microsoft.OperationalInsights/workspaces@2025-02-01' = {
  name: 'log-dvd-${environmentName}'
  location: location
  tags: tags
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
    workspaceCapping: { dailyQuotaGb: dailyQuotaGb }
  }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: 'appi-dvd-${environmentName}'
  location: location
  tags: tags
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId: workspace.id
    IngestionMode: 'LogAnalytics'
  }
}

output workspaceId string = workspace.id
output appInsightsConnectionString string = appInsights.properties.ConnectionString
