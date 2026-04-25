param location string
param name string
param appServicePrincipalId string
param cosmosEndpoint string
param storageAccountName string
param keyVaultName string
param auth0Domain string
param auth0Audience string

var keyVaultUri = 'https://${keyVaultName}.vault.azure.net'

resource store 'Microsoft.AppConfiguration/configurationStores@2024-05-01' = {
  name: name
  location: location
  sku: { name: 'standard' }
  properties: {
    disableLocalAuth: true
  }
}

// App Configuration Data Reader
resource dataReaderAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(store.id, appServicePrincipalId, 'appconfig-data-reader')
  scope: store
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '516239f1-63e7-40bd-9e9e-be997ecbc9c8')
    principalId: appServicePrincipalId
    principalType: 'ServicePrincipal'
  }
}

// Plain config keys
var plainKeys = [
  { key: 'PantryPunk:Sentinel', val: '1' }
  { key: 'CosmosDb:AccountEndpoint', val: cosmosEndpoint }
  { key: 'CosmosDb:DatabaseName', val: 'PantryPunkDb' }
  { key: 'BlobStorage:AccountName', val: storageAccountName }
  { key: 'BlobStorage:PhotosContainer', val: 'photos' }
  { key: 'PantryPunk:Claude:Model', val: 'claude-sonnet-4-6' }
  { key: 'PantryPunk:Claude:MaxTokensImage', val: '256' }
  { key: 'PantryPunk:Claude:MaxTokensVoice', val: '512' }
  { key: 'PantryPunk:RateLimit:AiRequestsPerMinute', val: '30' }
  { key: 'PantryPunk:RateLimit:ShareConfirmPerHour', val: '10' }
  { key: 'PantryPunk:RateLimit:PerIpPerMinute', val: '120' }
  { key: 'PantryPunk:ShareCode:ExpiryHours', val: '24' }
  { key: 'Auth0:Domain', val: auth0Domain }
  { key: 'Auth0:Audience', val: auth0Audience }
]

resource plainKvs 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = [for entry in plainKeys: {
  parent: store
  name: entry.key
  properties: {
    value: entry.val
  }
}]

// Key Vault reference entries
var kvRefKeys = [
  { key: 'Claude:ApiKey', secret: 'Claude--ApiKey' }
  { key: 'RevenueCat:WebhookSecret', secret: 'RevenueCat--WebhookSecret' }
]

resource kvRefs 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = [for entry in kvRefKeys: {
  parent: store
  name: entry.key
  properties: {
    value: '{"uri":"${keyVaultUri}/secrets/${entry.secret}"}'
    contentType: 'application/vnd.microsoft.appconfig.keyvaultref+json;charset=utf-8'
  }
}]

// Feature flags (targeting filter pre-configured for subscribers group)
var featureFlags = ['TalkIt', 'RealtimeSync', 'AnnualSubscription', 'AppAttest']

resource flags 'Microsoft.AppConfiguration/configurationStores/keyValues@2024-05-01' = [for flag in featureFlags: {
  parent: store
  name: '.appconfig.featureflag~2F${flag}'
  properties: {
    value: '{"id":"${flag}","description":"","enabled":false,"conditions":{"client_filters":[{"name":"Microsoft.Targeting","parameters":{"Audience":{"Groups":[{"Name":"subscribers","RolloutPercentage":0}],"DefaultRolloutPercentage":0}}}]}}'
    contentType: 'application/vnd.microsoft.appconfig.ff+json;charset=utf-8'
  }
}]

output endpoint string = store.properties.endpoint
