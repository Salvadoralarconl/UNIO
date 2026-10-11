#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
exec docker compose --project-name "${UNIO_COMPOSE_PROJECT:-unio}" --env-file .env.production -f compose.production.yaml "$@"
