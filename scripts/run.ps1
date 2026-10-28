# PostgreSQL, Redis, and ShortLink on http://localhost:5276 (API reference at /docs).
Set-Location (Join-Path $PSScriptRoot "..")
docker compose -f infra/docker-compose.yml up --build @args
