targetScope = 'subscription'

param location string
param env string
param auth0Domain string
param auth0Audience string
param appServicePlanSku string = 'B1'
param logRetentionDays int = 30
// Pass 'api.pantrypunk.ai' only after DNS (CNAME + asuid TXT) is configured
param apiCustomHostname string = ''

var prefix = 'pp'
var resourceGroupName = '${prefix}-rg-${env}'
var logName = '${prefix}-log-${env}'
var appInsightsName = '${prefix}-appi-${env}'
var keyVaultName = '${prefix}-kv-${env}-${take(uniqueString(subscription().subscriptionId), 6)}'
var cosmosName = '${prefix}-cosmos-${env}'
var cosmosDatabaseName = 'PantryPunkDb'
var photosContainer = 'photos'
var storageAccountName = '${prefix}st${env}'
var appPlanName = '${prefix}-plan-${env}'
var appSiteName = '${prefix}-app-${env}'
var cosmosEndpoint = 'https://${cosmosName}.documents.azure.com:443/'
var keyVaultUri = 'https://${keyVaultName}${environment().suffixes.keyvaultDns}'
var seedUamiName = '${prefix}-seed-uami-${env}'
var seedScriptName = '${prefix}-seed-admin-${env}'
var seedAppConfigUamiName = '${prefix}-seed-appcfg-uami-${env}'
var seedAppConfigScriptName = '${prefix}-seed-appcfg-${env}'

resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
}

module logAnalytics 'modules/logAnalytics.bicep' = {
  name: 'logAnalytics'
  scope: rg
  params: {
    location: location
    name: logName
    retentionInDays: logRetentionDays
  }
}

module appInsights 'modules/appInsights.bicep' = {
  name: 'appInsights'
  scope: rg
  params: {
    location: location
    name: appInsightsName
    workspaceId: logAnalytics.outputs.workspaceId
  }
}

module appService 'modules/appService.bicep' = {
  name: 'appService'
  scope: rg
  params: {
    location: location
    planName: appPlanName
    siteName: appSiteName
    sku: appServicePlanSku
    cosmosEndpoint: cosmosEndpoint
    cosmosDatabaseName: cosmosDatabaseName
    storageAccountName: storageAccountName
    photosContainer: photosContainer
    auth0Domain: auth0Domain
    auth0Audience: auth0Audience
    keyVaultUri: keyVaultUri
    appInsightsConnectionString: appInsights.outputs.connectionString
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceId
    customHostname: apiCustomHostname
  }
}

module keyVault 'modules/keyVault.bicep' = {
  name: 'keyVault'
  scope: rg
  params: {
    location: location
    name: keyVaultName
    appServicePrincipalId: appService.outputs.principalId
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceId
  }
}

module cosmos 'modules/cosmos.bicep' = {
  name: 'cosmos'
  scope: rg
  params: {
    location: location
    accountName: cosmosName
    databaseName: cosmosDatabaseName
    appServicePrincipalId: appService.outputs.principalId
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceId
  }
}

module seedAdmin 'modules/seedAdminUser.bicep' = {
  name: 'seedAdminUser'
  scope: rg
  params: {
    location: location
    cosmosAccountName: cosmosName
    uamiName: seedUamiName
    scriptName: seedScriptName
  }
  dependsOn: [
    cosmos
  ]
}

module seedAppConfig 'modules/seedAppConfig.bicep' = {
  name: 'seedAppConfig'
  scope: rg
  params: {
    location: location
    cosmosAccountName: cosmosName
    databaseName: cosmosDatabaseName
    uamiName: seedAppConfigUamiName
    scriptName: seedAppConfigScriptName
  }
  dependsOn: [
    cosmos
  ]
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  scope: rg
  params: {
    location: location
    accountName: storageAccountName
    containerName: photosContainer
    appServicePrincipalId: appService.outputs.principalId
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceId
  }
}

module customDomain 'modules/customDomain.bicep' = if (!empty(apiCustomHostname)) {
  name: 'customDomain'
  scope: rg
  params: {
    location: location
    siteName: appSiteName
    planId: appService.outputs.planId
    customHostname: apiCustomHostname
  }
}

output resourceGroupName string = rg.name
output appServiceHostname string = appService.outputs.defaultHostname
output customDomainVerificationId string = appService.outputs.customDomainVerificationId
output keyVaultName string = keyVault.outputs.keyVaultName
output cosmosEndpoint string = cosmos.outputs.endpoint
