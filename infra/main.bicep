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
var appConfigName = '${prefix}-appcs-${env}'
var cosmosName = '${prefix}-cosmos-${env}'
var storageAccountName = '${prefix}st${env}'
var appPlanName = '${prefix}-plan-${env}'
var appSiteName = '${prefix}-app-${env}'
var appConfigEndpoint = 'https://${appConfigName}.azconfig.io'
var seedUamiName = '${prefix}-seed-uami-${env}'
var seedScriptName = '${prefix}-seed-admin-${env}'

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
    databaseName: 'PantryPunkDb'
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
output customDomainVerificationId string = appService.outputs.customDomainVerificationId
output appConfigEndpoint string = appConfig.outputs.endpoint
output keyVaultName string = keyVault.outputs.keyVaultName
output cosmosEndpoint string = cosmos.outputs.endpoint
