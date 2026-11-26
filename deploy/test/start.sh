#!/usr/bin/env bash
# Starts the image as it will be deployed (compose.yml) and checks what only a deployment has: it starts without database, the
# migrations bundle, the user of the application, the demo loader and --check. CI runs it, and then E2E and ZAP against it.
#   DISPATCHGUARD_IMAGE=<image> [DISPATCHGUARD_PORT=8443] [WEEK=<monday>] deploy/test/start.sh
#   docker compose -f deploy/test/compose.yml down --volumes     (afterwards)
set -euo pipefail
cd "$(dirname "$0")"
: "${DISPATCHGUARD_IMAGE:?the image to test}"
export DISPATCHGUARD_IMAGE DISPATCHGUARD_PORT="${DISPATCHGUARD_PORT:-8443}"
# Git Bash on Windows would rewrite the paths that are of the containers.
export MSYS_NO_PATHCONV=1

umask 077
mkdir -p certs
openssl req -x509 -newkey rsa:2048 -nodes -days 2 -subj "/CN=dispatchguard.test" -addext "subjectAltName=DNS:dispatchguard.test" \
  -keyout certs/key.pem -out certs/cert.pem 2>/dev/null
# The users of the containers are others.
chmod 755 certs
chmod 644 certs/cert.pem certs/key.pem
{
  echo "DISPATCHGUARD_IMAGE=$DISPATCHGUARD_IMAGE"
  echo "DISPATCHGUARD_PORT=$DISPATCHGUARD_PORT"
  echo "OWNER_PASSWORD=$(openssl rand -hex 16)"
  echo "APPLICATION_PASSWORD=$(openssl rand -hex 16)"
  echo "RESET_KEY=$(openssl rand -hex 16)"
  echo "SESSION_KEY=$(openssl rand -base64 32)"
  echo "CRON_SECRET=$(openssl rand -hex 16)"
} > .env
# shellcheck disable=SC1091
source .env

# Without it the session of an earlier run would open what must be closed.
rm -f certs/cookies

status_of() {
  local url="$1"
  shift
  curl --silent --output /dev/null --write-out '%{http_code}' --max-time 20 --cacert certs/cert.pem \
    --cookie certs/cookies --cookie-jar certs/cookies \
    --resolve "dispatchguard.test:$DISPATCHGUARD_PORT:127.0.0.1" "$@" "https://dispatchguard.test:$DISPATCHGUARD_PORT$url" || true
}

expect() {
  local status
  status="$(status_of "$2")"
  echo "$2 → $status (expected $1)"
  [ "$status" = "$1" ]
}

wait_for() {
  for _ in $(seq 1 60); do
    if [ "$(status_of "$2")" = "$1" ]; then expect "$1" "$2"; return; fi
    sleep 2
  done
  expect "$1" "$2"
}

sql() {
  # By TCP: while the image prepares the database, its server only listens on the socket.
  docker compose exec -T -e PGPASSWORD="$OWNER_PASSWORD" database psql -h 127.0.0.1 -U postgres -v ON_ERROR_STOP=1 "$@"
}

echo "== Without database"
docker compose up -d app proxy
wait_for 200 /health/live
expect 200 /
expect 401 /health/ready
expect 401 /api/admin/demo/rebuild
expect 200 /api/session
expect 503 /health/ready

echo "== Database, migrations and user of the application"
docker compose up -d database
for _ in $(seq 1 60); do
  if sql -c "SELECT 1" >/dev/null 2>&1; then break; fi
  sleep 2
done
docker compose run --rm migrations
sql -d DispatchGuard -f /sql/application-role.sql
sql -d DispatchGuard -v ApplicationLogin=lof_app -v ApplicationPassword="$APPLICATION_PASSWORD" -f /sql/application-login.sql
wait_for 200 /health/ready

echo "== Rebuild of the demo, as the cron asks for it"
status="$(status_of /api/admin/demo/rebuild --max-time 300 --header "Authorization: Bearer $CRON_SECRET")"
echo "/api/admin/demo/rebuild → $status (expected 200)"
[ "$status" = 200 ]

echo "== Timeline of the visit, as the user of the application"
status="$(status_of /api/visits --header "X-DispatchGuard-Request: 1" --json '{"Events":[{"AgoMs":0,"Kind":"Screen","Name":"Prueba"}]}')"
echo "/api/visits → $status (expected 204)"
[ "$status" = 204 ]
expect 404 /api/audit/visits

echo "== Demo"
docker compose run --rm sample-data --reset-key "$RESET_KEY" ${WEEK:+--week "$WEEK"}
docker compose run --rm sample-data --check
