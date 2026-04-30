---
description: Publish PantryPunk.Api and zip-deploy it to Azure App Service
allowed-tools: Bash, Read
---

Deploy the PantryPunk.Api application code (no infra changes) to Azure App Service `pp-app-prod` in resource group `pp-rg-prod`.

## Preflight

1. Run `az account show --query '{sub:name, user:user.name}' -o tsv`. If it fails, stop and ask the user to `az login`.
2. Run `git status --short` and `git rev-parse --abbrev-ref HEAD`. If the tree is dirty, warn the user and confirm before continuing.
3. Echo the target (subscription, resource group `pp-rg-prod`, app `pp-app-prod`) and **wait for explicit confirmation** before running `az webapp deploy`.

## Steps

```bash
# 1. Build a Release publish bundle
dotnet publish PantryPunk.Api/PantryPunk.Api.csproj -c Release -o publish

# 2. Repackage into app.zip (overwrites the existing one)
#    Bash:       (cd publish && zip -r ../app.zip .)
#    PowerShell: Compress-Archive -Path publish/* -DestinationPath app.zip -Force
#    Use whichever shell the user is in; this repo is on Windows with bash available.

# 3. Deploy
az webapp deploy \
  --resource-group pp-rg-prod \
  --name pp-app-prod \
  --src-path app.zip \
  --type zip
```

## Post-deploy

- Tail logs briefly to confirm startup: `az webapp log tail -g pp-rg-prod -n pp-app-prod` (run in background, stop after a clean startup line).
- Smoke test: `curl -i https://pp-app-prod.azurewebsites.net/health` (or the custom domain if configured).

## Reporting

Report in 2–4 lines: branch deployed, App Service URL, deployment status, and any warnings from `dotnet publish` or the log tail. Do NOT print secrets or full configuration dumps.
