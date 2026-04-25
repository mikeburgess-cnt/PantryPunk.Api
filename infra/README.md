# PantryPunk Infrastructure

Bicep templates that provision all Azure resources for PantryPunk.Api using managed identity throughout.

## Resources created

| Name | Type |
|---|---|
| `pp-log-prod` | Log Analytics workspace |
| `pp-appi-prod` | Application Insights (workspace-based) |
| `pp-kv-prod` | Key Vault (RBAC auth, purge-protected) |
| `pp-appcs-prod` | App Configuration (Standard) |
| `pp-cosmos-prod` | Cosmos DB (serverless, SQL API) |
| `ppstprod` | Storage account (private blobs, no shared key) |
| `pp-plan-prod` | App Service Plan (Linux B1) |
| `pp-app-prod` | App Service (.NET 10, system-assigned MI) |

## Prerequisites

- Azure CLI ≥ 2.60 with Bicep extension
- Resource group already created (or create one with the command below)
- Your Auth0 domain and audience values

## Deploy

```bash
# 1. Check that .NET 10 is available on Linux App Service in australiaeast
az webapp list-runtimes --os linux | grep -i 10

# 2. Fill in your Auth0 values in main.bicepparam, then validate
az bicep build --file infra/main.bicep
az deployment sub what-if --location australiaeast -f infra/main.bicep -p infra/main.bicepparam

# 3. Deploy (creates pp-rg-prod resource group and all resources)
az deployment sub create --location australiaeast -f infra/main.bicep -p infra/main.bicepparam
```

## Post-deploy: seed Key Vault secrets

```bash
az keyvault secret set --vault-name pp-kv-prod --name Claude--ApiKey            --value <YOUR_CLAUDE_KEY>
az keyvault secret set --vault-name pp-kv-prod --name RevenueCat--WebhookSecret --value <YOUR_REVENUECAT_SECRET>
```

Secret names use `--` (double hyphen) which maps to `:` in ASP.NET Core config.

## Trigger an App Configuration refresh after any key change

```bash
az appconfig kv set --name pp-appcs-prod --key 'PantryPunk:Sentinel' --value 2 --yes
```

Increment the value each time — the app polls for changes every 5 minutes and reloads all config when the sentinel changes.

## Notes

- **Voice endpoint deferred:** Azure AI Speech (Cognitive Services) is not provisioned here. The `/api/shopping-list/items/voice` endpoint will fail until Speech is added and `AzureSpeech:Key` is seeded in Key Vault.
- **Custom domain:** `api.pantrypunk.ai` requires DNS verification post-deploy via `az webapp config hostname add`.
- **Feature flags:** All four flags (`TalkIt`, `RealtimeSync`, `AnnualSubscription`, `AppAttest`) are seeded disabled. Enable them via the Azure portal or `az appconfig feature enable`.
- **Cosmos data-plane RBAC:** Provisioned in `cosmos.bicep` via `sqlRoleAssignments` (not ARM RBAC). If the role assignment is missing, the app will log 403 errors from `DefaultAzureCredential`.
