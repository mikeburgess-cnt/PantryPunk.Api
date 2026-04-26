targetScope = 'subscription'

param location string
param env string
param auth0Domain string
param auth0Audience string
param appServicePlanSku string = 'B1'
param logRetentionDays int = 30

var prefix = 'pp'
var resourceGroupName = '${prefix}-rg-${env}'
var logName = '${prefix}-log-${env}'
var appInsightsName = '${prefix}-appi-${env}'
var keyVaultName = '${prefix}-kv-${env}-${take(uniqueString(subscription().subscriptionId), 6)}'
var appConfigName = '${prefix}-appcs-${env}'
var cosmosName = '${prefix}-cosmos-${env}'
var storageAccountName = '${prefix}st${env}'
var appPlanName = '${prefix}-plan-${env}'
var appSiteName = '${prefix}-app-${env}'
var appConfigEndpoint = 'https://${appConfigName}.azconfig.io'

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
    appConfigEndpoint: appConfigEndpoint
    appInsightsConnectionString: appInsights.outputs.connectionString
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceId
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
    databaseName: 'PantryPunkDb'
    appServicePrincipalId: appService.outputs.principalId
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceId
  }
}

module storage 'modules/storage.bicep' = {
  name: 'storage'
  scope: rg
  params: {
    location: location
    accountName: storageAccountName
    containerName: 'photos'
    appServicePrincipalId: appService.outputs.principalId
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceId
  }
}

module appConfig 'modules/appConfig.bicep' = {
  name: 'appConfig'
  scope: rg
  params: {
    location: location
    name: appConfigName
    appServicePrincipalId: appService.outputs.principalId
    cosmosEndpoint: cosmos.outputs.endpoint
    storageAccountName: storageAccountName
    keyVaultName: keyVaultName
    auth0Domain: auth0Domain
    auth0Audience: auth0Audience
    logAnalyticsWorkspaceId: logAnalytics.outputs.workspaceId
  }
}

output resourceGroupName string = rg.name
output appServiceHostname string = appService.outputs.defaultHostname
output appConfigEndpoint string = appConfig.outputs.endpoint
output keyVaultName string = keyVault.outputs.keyVaultName
output cosmosEndpoint string = cosmos.outputs.endpoint
