---
description: Deploy PantryPunk Azure infrastructure via Bicep (subscription-scoped)
allowed-tools: Bash, Read
---

Deploy the PantryPunk Azure infrastructure defined in `infra/main.bicep` with `infra/main.bicepparam`. This is a subscription-scoped deployment that creates/updates the `pp-rg-prod` resource group and every resource inside it (App Service, Cosmos, Key Vault, Storage, App Insights, Log Analytics).

## Preflight

1. Run `az account show --query '{sub:name, user:user.name}' -o tsv`. If it fails, stop and ask the user to `az login`. Confirm the subscription is correct before going further.
2. Read `infra/main.bicepparam` and show the user the `auth0Domain`, `auth0Audience`, `appServicePlanSku`, and `apiCustomHostname` values that will be applied. Confirm before continuing.
3. Note: pass a non-empty `apiCustomHostname` only after DNS (CNAME + `asuid` TXT) is in place — otherwise the `customDomain` module will fail.

## Steps

```bash
# 1. Build / lint
az bicep build --file infra/main.bicep

# 2. What-if — show the diff to the user and WAIT for explicit confirmation
az deployment sub what-if \
  --location australiaeast \
  -f infra/main.bicep \
  -p infra/main.bicepparam
```

**Stop here, summarise the what-if output (created / modified / deleted resources), and ask the user to confirm before applying.**

```bash
# 3. Apply
az deployment sub create \
  --location australiaeast \
  -f infra/main.bicep \
  -p infra/main.bicepparam
```

## Post-deploy

- Capture the outputs (`resourceGroupName`, `appServiceHostname`, `keyVaultName`, `cosmosEndpoint`, `customDomainVerificationId`) from the deployment result and report them.
- If this is the first deploy, remind the user to run `/pp-deploy-secrets` next to seed Key Vault, then `/pp-deploy-app` to ship the application code. The `app-config` Cosmos document is auto-seeded by the `seedAppConfig` module on first deploy (create-if-not-exists from `infra/seed/app-config.json`).
- Cosmos data-plane RBAC is provisioned via `sqlRoleAssignments` in `cosmos.bicep`. If the app later logs 403s from `DefaultAzureCredential`, the role assignment is the first thing to check.

## Reporting

Report in 4–6 lines: deployment name, status, key outputs (App Service hostname, Key Vault name), and any non-fatal warnings. Do not paste the full ARM response.
