---
description: Seed or rotate Key Vault secrets for PantryPunk.Api
argument-hint: [claude | revenuecat | all]
allowed-tools: Bash, Read
---

Seed or rotate secrets in the PantryPunk Key Vault. The argument selects which secret(s) to set; default to `all` if no argument is given.

Argument: `$ARGUMENTS`

## Secrets managed by this command

| Secret name in Key Vault | Maps to ASP.NET config key |
|---|---|
| `Claude--ApiKey` | `Claude:ApiKey` |
| `RevenueCat--WebhookSecret` | `RevenueCat:WebhookSecret` |

The `--` (double hyphen) is intentional — Key Vault forbids `:`, and the configuration provider rewrites `--` to `:`.

## Preflight

1. Run `az account show --query '{sub:name, user:user.name}' -o tsv`. If it fails, stop and ask the user to `az login`.
2. Discover the Key Vault name (the suffix is a hash of the subscription id):
   ```bash
   KV=$(az keyvault list -g pp-rg-prod --query '[0].name' -o tsv)
   echo "$KV"
   ```
   If more than one vault is returned or `$KV` is empty, stop and ask the user which vault to target.
3. Confirm with the user that `$KV` is correct before writing.

## Collecting the secret values

**Never have the user paste secret values into chat.** Ask them to either:
- Place each secret in a local file path they tell you (e.g. `~/.pp-secrets/claude.txt`), and read it with `--file`, OR
- Set an env var in their shell (e.g. `CLAUDE_API_KEY`) and pass `--value "$CLAUDE_API_KEY"`.

Use `--file` over `--value` whenever a file path is available — it avoids the value showing up in shell history.

## Steps

For `claude` (or `all`):
```bash
az keyvault secret set --vault-name "$KV" --name Claude--ApiKey --file <path-to-claude-key>
```

For `revenuecat` (or `all`):
```bash
az keyvault secret set --vault-name "$KV" --name RevenueCat--WebhookSecret --file <path-to-revenuecat-secret>
```

## Post-set: trigger a config reload

The app polls App Configuration every 5 minutes and reloads when the sentinel changes. After rotating a secret, bump the sentinel:

```bash
CUR=$(az appconfig kv show --name pp-appcs-prod --key 'PantryPunk:Sentinel' --query value -o tsv)
az appconfig kv set --name pp-appcs-prod --key 'PantryPunk:Sentinel' --value $((CUR + 1)) --yes
```

## Reporting

Report which secrets were set (by name only — never echo values), the vault name, the new sentinel value, and confirm the user that the app will pick up the change within ~5 minutes (or sooner if they restart the App Service).
