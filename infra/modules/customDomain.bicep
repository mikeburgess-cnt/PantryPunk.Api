param location string
param siteName string
param planId string
param customHostname string

resource site 'Microsoft.Web/sites@2023-12-01' existing = {
  name: siteName
}

// App Service Managed Certificate — free, auto-renewed by Azure
// Requires: hostname already bound to site + CNAME pointing at the site
resource certificate 'Microsoft.Web/certificates@2023-12-01' = {
  name: '${replace(customHostname, '.', '-')}-cert'
  location: location
  properties: {
    serverFarmId: planId
    canonicalName: customHostname
  }
}

resource sslBinding 'Microsoft.Web/sites/hostNameBindings@2023-12-01' = {
  parent: site
  name: customHostname
  dependsOn: [certificate]
  properties: {
    hostNameType: 'Verified'
    sslState: 'SniEnabled'
    thumbprint: certificate.properties.thumbprint
  }
}
