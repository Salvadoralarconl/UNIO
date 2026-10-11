#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
umask 077
backup_root="${UNIO_BACKUP_ROOT:-$HOME/.local/share/unio/backups}"
destination="$backup_root/$(date -u +%Y%m%dT%H%M%SZ)"
mkdir -p "$destination"
chmod 700 "$destination"
dc=(bash deploy/compose.sh)
api_running=$("${dc[@]}" ps --status running -q api)
web_running=$("${dc[@]}" ps --status running -q web)
resume() {
  if [[ -n "$api_running" ]]; then "${dc[@]}" start api; fi
  if [[ -n "$web_running" ]]; then "${dc[@]}" start web; fi
}
trap resume EXIT
"${dc[@]}" stop web api
"${dc[@]}" exec -T database pg_dump -U unio -d unio -Fc > "$destination/database.dump"
"${dc[@]}" run --rm --no-deps -T --entrypoint sh api \
  -c 'tar -czf - -C /app uploads keys private -C /run tls' > "$destination/files.tar.gz"
cp .env.production "$destination/config.env"
git rev-parse HEAD > "$destination/source-commit.txt"
"${dc[@]}" images > "$destination/images.txt"
sha256sum "$destination/database.dump" "$destination/files.tar.gz" > "$destination/SHA256SUMS"
echo "Private paired backup saved to $destination. It contains data and credentials; never upload it to GitHub."
