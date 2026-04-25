param location string
param name string
param appServicePrincipalId string

resource kv 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: name
  location: location
  properties: {
    sku: { family: 'A', name: 'standard' }
    tenantId: subscription().tenantId
    enableRbacAuthorization: true
    enableSoftDelete: true
    softDeleteRetentionInDays: 90
    enablePurgeProtection: true
  }
}

// Key Vault Secrets User — allows App Service MI to read secrets (used by App Config KV refs)
resource secretsUserAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(kv.id, appServicePrincipalId, 'kv-secrets-user')
  scope: kv
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
    principalId: appServicePrincipalId
    principalType: 'ServicePrincipal'
  }
}

output keyVaultName string = kv.name
output keyVaultUri string = kv.properties.vaultUri
