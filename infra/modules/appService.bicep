param location string
param planName string
param siteName string
param sku string
param appConfigEndpoint string
param appInsightsConnectionString string

resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: planName
  location: location
  kind: 'linux'
  sku: {
    name: sku
  }
  properties: {
    reserved: true
  }
}

resource site 'Microsoft.Web/sites@2023-12-01' = {
  name: siteName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'DOTNETCORE|10.0'
      http20Enabled: true
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      healthCheckPath: '/health'
      appSettings: [
        { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
        { name: 'AzureAppConfiguration__Endpoint', value: appConfigEndpoint }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        { name: 'ApplicationInsightsAgent_EXTENSION_VERSION', value: '~3' }
        { name: 'WEBSITE_RUN_FROM_PACKAGE', value: '1' }
      ]
    }
  }
}

output principalId string = site.identity.principalId
output defaultHostname string = site.properties.defaultHostName
