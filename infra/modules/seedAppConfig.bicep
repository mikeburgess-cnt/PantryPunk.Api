param location string
param cosmosAccountName string
param databaseName string = 'PantryPunkDb'
param containerName string = 'AppConfig'
param uamiName string
param scriptName string

var seedDocument = loadTextContent('../seed/app-config.json')

resource cosmosAccount 'Microsoft.DocumentDB/databaseAccounts@2024-05-15' existing = {
  name: cosmosAccountName
}

resource uami 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: uamiName
  location: location
}

resource seedRoleAssignment 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2024-05-15' = {
  parent: cosmosAccount
  name: guid(cosmosAccount.id, uami.id, 'cosmos-data-contributor-seed-appconfig')
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
    // Stable tag — the script body itself is create-if-not-exists, so even on re-runs an existing
    // doc is left untouched. Bump this string only if you need to force a re-run after manually
    // deleting the doc from Cosmos.
    forceUpdateTag: 'v1'
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
DOC_ID=$(echo "$BODY" | jq -r '.id')
RESOURCE="${COSMOS_ENDPOINT%/}"
RESOURCE="${RESOURCE%:443}"
TOKEN=$(az account get-access-token --resource "$RESOURCE" --query accessToken -o tsv)
export DOC_ID RESOURCE TOKEN
python3 <<'PY'
import os, urllib.parse, urllib.request, urllib.error, email.utils, time
body = os.environ["BODY"]
doc_id = os.environ["DOC_ID"]
resource = os.environ["RESOURCE"]
db = os.environ["DATABASE_NAME"]
coll = os.environ["CONTAINER_NAME"]
token = os.environ["TOKEN"]
sig = urllib.parse.quote(f"type=aad&ver=1.0&sig={token}")

def cosmos_request(method, path, payload=None):
    date_hdr = email.utils.formatdate(time.time(), usegmt=True)
    url = f"{resource}{path}"
    headers = {
        "Authorization": sig,
        "x-ms-version": "2018-12-31",
        "x-ms-date": date_hdr,
        "x-ms-documentdb-partitionkey": f'["{doc_id}"]',
    }
    data = None
    if payload is not None:
        headers["Content-Type"] = "application/json"
        data = payload.encode()
    req = urllib.request.Request(url, data=data, method=method, headers=headers)
    return urllib.request.urlopen(req)

try:
    with cosmos_request("GET", f"/dbs/{db}/colls/{coll}/docs/{doc_id}") as r:
        print(f"app-config already exists (status={r.status}); not seeding")
        raise SystemExit(0)
except urllib.error.HTTPError as e:
    if e.code != 404:
        print(f"HTTP {e.code} on read: {e.read().decode()}")
        raise

try:
    with cosmos_request("POST", f"/dbs/{db}/colls/{coll}/docs", payload=body) as r:
        print(f"seeded app-config: status={r.status}")
except urllib.error.HTTPError as e:
    print(f"HTTP {e.code} on create: {e.read().decode()}")
    raise
PY
'''
  }
  dependsOn: [
    seedRoleAssignment
  ]
}
