#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
umask 077
if [[ -e .env.production ]]; then echo '.env.production exists; kept unchanged.' >&2; exit 1; fi
read -r -p 'Private Ollama origin reachable from Docker (http://IP:11434): ' ollama_url
if [[ ! "$ollama_url" =~ ^https?://[a-zA-Z0-9.-]+(:[0-9]+)?/?$ ]]; then
  echo 'Use a private origin without credentials, paths or query strings.' >&2; exit 1
fi
password=$(openssl rand -hex 32)
release=$(git rev-parse --short=12 HEAD)
(set -o noclobber; printf 'POSTGRES_PASSWORD=%s\nOLLAMA_BASE_URL=%s\nUNIO_RELEASE=%s\n' "$password" "$ollama_url" "$release" > .env.production)
unset password
echo 'Created private .env.production. Never commit or share this file.'
