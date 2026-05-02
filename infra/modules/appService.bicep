param location string
param planName string
param siteName string
param sku string
param cosmosEndpoint string
param cosmosDatabaseName string
param storageAccountName string
param photosContainer string
param auth0Domain string
param auth0Audience string
param keyVaultUri string
param appInsightsConnectionString string
param logAnalyticsWorkspaceId string
param customHostname string = ''

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
        { name: 'CosmosDb__AccountEndpoint', value: cosmosEndpoint }
        { name: 'CosmosDb__DatabaseName', value: cosmosDatabaseName }
        { name: 'BlobStorage__AccountName', value: storageAccountName }
        { name: 'BlobStorage__PhotosContainer', value: photosContainer }
        { name: 'Auth0__Domain', value: auth0Domain }
        { name: 'Auth0__Audience', value: auth0Audience }
        { name: 'KeyVault__Uri', value: keyVaultUri }
        { name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsightsConnectionString }
        { name: 'ApplicationInsightsAgent_EXTENSION_VERSION', value: '~3' }
        { name: 'WEBSITE_RUN_FROM_PACKAGE', value: '1' }
      ]
    }
  }
}

resource scmCredentialsPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2023-12-01' = {
  parent: site
  name: 'scm'
  properties: {
    allow: false
  }
}

resource ftpCredentialsPolicy 'Microsoft.Web/sites/basicPublishingCredentialsPolicies@2023-12-01' = {
  parent: site
  name: 'ftp'
  properties: {
    allow: false
  }
}

// Binds the custom hostname without SSL — cert and SSL binding are handled by customDomain.bicep
// DNS (CNAME + asuid TXT) must exist before this binding succeeds on deployment
resource hostNameBinding 'Microsoft.Web/sites/hostNameBindings@2023-12-01' = if (!empty(customHostname)) {
  parent: site
  name: !empty(customHostname) ? customHostname : 'placeholder'
  properties: {
    hostNameType: 'Verified'
    sslState: 'Disabled'
  }
}

resource siteDiagnostics 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: 'site-diagnostics'
  scope: site
  properties: {
    workspaceId: logAnalyticsWorkspaceId
    logs: [
      { category: 'AppServiceHTTPLogs', enabled: true }
      { category: 'AppServiceConsoleLogs', enabled: true }
      { category: 'AppServiceAuditLogs', enabled: true }
      { category: 'AppServiceIPSecAuditLogs', enabled: true }
      { category: 'AppServiceAppLogs', enabled: true }
    ]
    metrics: [
      { category: 'AllMetrics', enabled: true }
    ]
  }
}

output principalId string = site.identity.principalId
output defaultHostname string = site.properties.defaultHostName
output customDomainVerificationId string = site.properties.customDomainVerificationId
output planId string = plan.id
