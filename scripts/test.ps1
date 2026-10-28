param([switch]$E2E)
Set-Location (Join-Path $PSScriptRoot "..")
dotnet test ShortLink.slnx
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($E2E -or $env:E2E -eq "1") {
  & (Join-Path $PSScriptRoot "e2e.ps1") -SkipInstall
  exit $LASTEXITCODE
}
