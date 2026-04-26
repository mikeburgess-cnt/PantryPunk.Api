param location string
param cosmosAccountName string
param databaseName string = 'PantryPunkDb'
param containerName string = 'Users'
param uamiName string
param scriptName string

var seedDocument = loadTextContent('../seed-admin.json')

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' existing = {
  name: cosmosAccountName
}

resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: uamiName
  location: location
}

// Cosmos DB Built-in Data Contributor for the seed UAMI
resource seedRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, uami.id, 'cosmos-data-contributor-seed')
  properties: {
    roleDefinitionId: '${cosmosAccount.id}/sqlRoleDefinitions/00000000-0000-0000-0000-000000000002'
    principalId: uami.properties.principalId
    scope: cosmosAccount.id
  }
}

resource seedScript 'Microsoft.Resources/deploymentScripts@2023-08-01' = {
  name: scriptName
  location: location
  kind: 'AzureCLI'
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: {
      '${uami.id}': {}
    }
  }
  properties: {
    azCliVersion: '2.61.0'
    forceUpdateTag: uniqueString(seedDocument)
    retentionInterval: 'PT1H'
    timeout: 'PT10M'
    cleanupPreference: 'OnSuccess'
    environmentVariables: [
      { name: 'COSMOS_ENDPOINT', value: cosmosAccount.properties.documentEndpoint }
      { name: 'DATABASE_NAME', value: databaseName }
      { name: 'CONTAINER_NAME', value: containerName }
      { name: 'BODY', value: seedDocument }
    ]
    scriptContent: '''
set -euo pipefail
USER_ID=$(echo "$BODY" | jq -r '.userId')
RESOURCE="${COSMOS_ENDPOINT%/}"
RESOURCE="${RESOURCE%:443}"
TOKEN=$(az account get-access-token --resource "$RESOURCE" --query accessToken -o tsv)
export USER_ID RESOURCE TOKEN
python3 <<'PY'
import os, urllib.parse, urllib.request, urllib.error, email.utils, time
body = os.environ["BODY"]
user_id = os.environ["USER_ID"]
resource = os.environ["RESOURCE"]
db = os.environ["DATABASE_NAME"]
coll = os.environ["CONTAINER_NAME"]
token = os.environ["TOKEN"]
sig = urllib.parse.quote(f"type=aad&ver=1.0&sig={token}")
date_hdr = email.utils.formatdate(time.time(), usegmt=True)
url = f"{resource}/dbs/{db}/colls/{coll}/docs"
req = urllib.request.Request(
    url,
    data=body.encode(),
    method="POST",
    headers={
        "Authorization": sig,
        "x-ms-version": "2018-12-31",
        "x-ms-date": date_hdr,
        "x-ms-documentdb-is-upsert": "true",
        "x-ms-documentdb-partitionkey": f'["{user_id}"]',
        "Content-Type": "application/json",
    },
)
try:
    with urllib.request.urlopen(req) as r:
        print(f"seeded user: {user_id} status={r.status}")
except urllib.error.HTTPError as e:
    print(f"HTTP {e.code}: {e.read().decode()}")
    raise
PY
'''
  }
  dependsOn: [
    seedRoleAssignment
  ]
}
