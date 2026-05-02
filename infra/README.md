# PantryPunk Infrastructure

Bicep templates that provision all Azure resources for PantryPunk.Api using managed identity throughout.

## Resources created

| Name | Type |
|---|---|
| `pp-log-prod` | Log Analytics workspace |
| `pp-appi-prod` | Application Insights (workspace-based) |
| `pp-kv-prod` | Key Vault (RBAC auth, purge-protected) |
| `pp-cosmos-prod` | Cosmos DB (serverless, SQL API) — also hosts the `AppConfig` runtime config doc |
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

## Admin user seed

`infra/seed-admin.json` is upserted into the Cosmos `Users` container during deployment by the `seedAdminUser` module. The seed runs via a `Microsoft.Resources/deploymentScripts` resource using a dedicated user-assigned managed identity that holds the `Cosmos DB Built-in Data Contributor` role on the account.

`forceUpdateTag` is derived from the JSON content, so the script only re-runs when `seed-admin.json` changes — edit the file and redeploy to update the seeded document. The same `id`/`userId` is used for upsert semantics, so re-runs are safe.

## Edit runtime config

Runtime config (Claude model, rate limits, share-code expiry, feature flags) lives in the `app-config` document in the Cosmos `AppConfig` container. The `seedAppConfig` Bicep module seeds this document **once** on first deploy (create-if-not-exists; never overwrites an existing doc), using `infra/seed/app-config.json` as the initial payload. After that, edit the doc via Data Explorer or your admin API — changes propagate on the next request after the 30-second middleware cache expires.

To force a re-seed after manually deleting the doc, bump the `forceUpdateTag` in `infra/modules/seedAppConfig.bicep` and redeploy.

## Known security gaps

- **Key Vault, Cosmos DB, and Storage are all publicly reachable over the internet.** Access is gated by AAD/RBAC only — no shared keys or connection strings are accepted. This is a reasonable posture for an initial deployment but not a full defence-in-depth posture.
- **Full network-level isolation requires:** VNet integration on App Service + Private Endpoints on each resource + `publicNetworkAccess: Disabled` on each resource. This is deferred but should be done before handling sensitive user data at scale.

## Notes

- **Custom domain:** `api.pantrypunk.ai` requires DNS verification post-deploy via `az webapp config hostname add`.
- **Feature flags:** All three flags (`RealtimeSync`, `AnnualSubscription`, `AppAttest`) are seeded disabled in `infra/seed/app-config.json`. Enable them by editing the `app-config` Cosmos document — set `FeatureManagement:<Flag>:EnabledFor:0:Parameters:Audience:DefaultRolloutPercentage` to `100`, or change the `subscribers` group rollout to target paid users.
- **Cosmos data-plane RBAC:** Provisioned in `cosmos.bicep` via `sqlRoleAssignments` (not ARM RBAC). If the role assignment is missing, the app will log 403 errors from `DefaultAzureCredential`.
