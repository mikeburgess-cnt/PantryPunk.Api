param location string
param name string
param retentionInDays int = 90

resource workspace 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: name
  location: location
  properties: {
    retentionInDays: retentionInDays
    sku: { name: 'PerGB2018' }
  }
}

output workspaceId string = workspace.id
