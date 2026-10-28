#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet test ShortLink.slnx
if [ "${E2E:-}" = "1" ]; then
  SKIP_INSTALL=1 ./scripts/e2e.sh
fi
