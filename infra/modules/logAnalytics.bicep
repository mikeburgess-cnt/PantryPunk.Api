param location string
param name string

resource workspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: name
  location: location
  properties: {
    retentionInDays: 30
    sku: { name: 'PerGB2018' }
  }
}

output workspaceId string = workspace.id
