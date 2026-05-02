@echo off
setlocal enabledelayedexpansion

REM Deploys PantryPunk Azure infrastructure (Bicep, subscription-scoped).
REM Mirrors the /pp-deploy-infra slash command.

pushd "%~dp0.."

echo === Verifying Azure login ===
az account show --query "{sub:name, user:user.name}" -o tsv
if errorlevel 1 (
    echo.
    echo ERROR: Not logged in. Run: az login
    popd
    exit /b 1
)

echo.
echo === Building Bicep template ===
az bicep build --file infra/main.bicep
if errorlevel 1 (
    echo ERROR: Bicep build failed.
    popd
    exit /b 1
)

echo.
echo === What-if (preview changes) ===
az deployment sub what-if --location australiaeast -f infra/main.bicep -p infra/main.bicepparam
if errorlevel 1 (
    echo ERROR: what-if failed.
    popd
    exit /b 1
)

echo.
echo ================================================
echo Review the what-if output above carefully.
echo ================================================
set /p CONFIRM=Type YES to apply the deployment:
if /i not "%CONFIRM%"=="YES" (
    echo Aborted by user.
    popd
    exit /b 0
)

echo.
echo === Applying deployment ===
az deployment sub create --location australiaeast -f infra/main.bicep -p infra/main.bicepparam
if errorlevel 1 (
    echo ERROR: Deployment failed.
    popd
    exit /b 1
)

echo.
echo === Done ===
echo If this was a first deploy, next steps:
echo   1. Seed Key Vault secrets (/pp-deploy-secrets)
echo   2. Deploy app code (pp-deploy-app.bat or /pp-deploy-app)
popd
endlocal
