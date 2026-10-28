#!/usr/bin/env bash
# PostgreSQL, Redis, and ShortLink on http://localhost:5276 (API reference at /docs).
set -euo pipefail
cd "$(dirname "$0")/.."
docker compose -f infra/docker-compose.yml up --build "$@"
