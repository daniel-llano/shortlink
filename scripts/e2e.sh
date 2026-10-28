#!/usr/bin/env bash
# Starts the Docker stack and runs the Playwright suite against the app.
set -euo pipefail
cd "$(dirname "$0")/.."

docker compose -f infra/docker-compose.yml up --build -d --wait

if ! command -v playwright >/dev/null 2>&1; then
  echo "Playwright CLI not found. Install it with: npm install -g playwright && playwright install chromium" >&2
  exit 1
fi
export NODE_PATH="$(npm root -g)"
if [ "${SKIP_INSTALL:-}" != "1" ]; then
  playwright install chromium
fi
playwright test -c e2e/playwright.config.cjs
