@echo off
setlocal enabledelayedexpansion

REM Deploys PantryPunk.Api code (no infra changes) to App Service pp-app-prod.
REM Mirrors the /pp-deploy-app slash command.

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
echo === Target ===
echo   Resource group: pp-rg-prod
echo   App Service:    pp-app-prod
echo.
set /p CONFIRM=Proceed with deployment? Type YES to continue:
if /i not "%CONFIRM%"=="YES" (
    echo Aborted.
    popd
    exit /b 0
)

echo.
echo === Building Release publish bundle ===
dotnet publish PantryPunk.Api\PantryPunk.Api.csproj -c Release -o publish
if errorlevel 1 (
    echo ERROR: dotnet publish failed.
    popd
    exit /b 1
)

echo.
echo === Packaging app.zip ===
if exist app.zip del /f /q app.zip
powershell -NoProfile -Command "Compress-Archive -Path publish/* -DestinationPath app.zip -Force"
if errorlevel 1 (
    echo ERROR: Compress-Archive failed.
    popd
    exit /b 1
)

echo.
echo === Deploying to Azure App Service ===
az webapp deploy --resource-group pp-rg-prod --name pp-app-prod --src-path app.zip --type zip
if errorlevel 1 (
    echo ERROR: az webapp deploy failed.
    popd
    exit /b 1
)

echo.
echo === Smoke test: GET /health ===
curl -i https://api.pantrypunk.ai/health

echo.
echo === Done ===
popd
endlocal
