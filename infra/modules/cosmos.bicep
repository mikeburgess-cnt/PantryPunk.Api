param location string
param accountName string
param databaseName string
param appServicePrincipalId string

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' = {
  name: accountName
  location: location
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    capabilities: [{ name: 'EnableServerless' }]
    locations: [{ locationName: location, failoverPriority: 0, isZoneRedundant: false }]
    disableLocalAuth: true
    publicNetworkAccess: 'Enabled'
    consistencyPolicy: { defaultConsistencyLevel: 'Session' }
  }
}

resource database 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2024-05-15' = {
  parent: cosmosAccount
  name: databaseName
  properties: {
    resource: { id: databaseName }
  }
}

resource usersContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'Users'
  properties: {
    resource: {
      id: 'Users'
      partitionKey: { paths: ['/userId'], kind: 'Hash' }
    }
  }
}

resource shoppingListsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'ShoppingLists'
  properties: {
    resource: {
      id: 'ShoppingLists'
      partitionKey: { paths: ['/listId'], kind: 'Hash' }
    }
  }
}

resource shareCodesContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2024-05-15' = {
  parent: database
  name: 'ShareCodes'
  properties: {
    resource: {
      id: 'ShareCodes'
      partitionKey: { paths: ['/code'], kind: 'Hash' }
    }
  }
}

// Cosmos DB Built-in Data Contributor (data-plane RBAC — not an ARM role assignment)
resource sqlRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, appServicePrincipalId, 'cosmos-data-contributor')
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: appServicePrincipalId
    scope: cosmosAccount.id
  }
}

output endpoint string = cosmosAccount.properties.documentEndpoint
