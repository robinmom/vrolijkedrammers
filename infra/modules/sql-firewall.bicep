// Firewallregels: alleen de (mogelijke) outbound-IP's van de API-app (docs/08 §5).
// Losse module omdat de IP's pas tijdens de deployment bekend zijn.
param sqlServerName string

@description('Mogelijke outbound-IP-adressen van de App Service.')
param ipAddresses array

resource server 'Microsoft.Sql/servers@2025-01-01' existing = {
  name: sqlServerName
}

resource rules 'Microsoft.Sql/servers/firewallRules@2025-01-01' = [
  for (ip, i) in ipAddresses: {
    parent: server
    name: 'appservice-outbound-${i}'
    properties: {
      startIpAddress: ip
      endIpAddress: ip
    }
  }
]
