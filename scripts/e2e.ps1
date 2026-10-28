# Starts the Docker stack and runs the Playwright suite against the app.
param([switch]$SkipInstall)
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")

docker compose -f infra/docker-compose.yml up --build -d --wait
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

if (-not (Get-Command playwright -ErrorAction SilentlyContinue)) {
  throw "Playwright CLI not found. Install it with: npm install -g playwright; playwright install chromium"
}
$env:NODE_PATH = (npm root -g | Select-Object -Last 1)
if (-not $SkipInstall) { playwright install chromium }
playwright test -c e2e/playwright.config.cjs
exit $LASTEXITCODE
