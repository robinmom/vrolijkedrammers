// Zet SNI aan op een bestaande hostnaam-binding met het (beheerde) certificaat. Losse module, omdat de binding eerst
// zonder certificaat moet bestaan voordat het certificaat kan worden uitgegeven.
param siteName string
param hostName string
param thumbprint string

resource site 'Microsoft.Web/sites@2025-03-01' existing = {
  name: siteName
}

resource binding 'Microsoft.Web/sites/hostNameBindings@2025-03-01' = {
  parent: site
  name: hostName
  properties: {
    siteName: siteName
    hostNameType: 'Verified'
    sslState: 'SniEnabled'
    thumbprint: thumbprint
    customHostNameDnsRecordType: length(split(hostName, '.')) > 2 ? 'CName' : 'A'
  }
}
