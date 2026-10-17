(
  set -e

  echo "========== 1. DOTNET BUILD =========="

  dotnet clean
  dotnet restore
  dotnet build
  dotnet test --no-restore

  echo "========== 2. LOAD ENV =========="

  set -a
  source .env
  set +a

  export SITE_DOMAIN=localhost
  export POSTGRES_DB="${POSTGRES_DB:-vibyroom}"
  export POSTGRES_USER="${POSTGRES_USER:-vibyroom}"

  if [[ -z "${POSTGRES_PASSWORD:-}" ]]; then
    export POSTGRES_PASSWORD="${VIBYROOM_POSTGRES_PASSWORD:-}"
  fi

  if [[ -z "${POSTGRES_PASSWORD:-}" ]]; then
    CONNECTION="${ConnectionStrings__DefaultConnection:-}"

    if [[ "$CONNECTION" == *Password=* ]]; then
      export POSTGRES_PASSWORD="${CONNECTION#*Password=}"
      export POSTGRES_PASSWORD="${POSTGRES_PASSWORD%%;*}"
    fi
  fi

  if [[ -z "${POSTGRES_PASSWORD:-}" ]]; then
    echo "ERROR: PostgreSQL password missing"
    exit 1
  fi

  if [[ -z "${GigaChat__AuthorizationKey:-}" ]]; then
    echo "ERROR: GigaChat key missing"
    exit 1
  fi

  echo "========== 3. VALIDATE COMPOSE =========="

  docker compose \
    -f compose.yaml \
    -f compose.local.yaml \
    -f compose.https.yaml \
    config --quiet

  echo "========== 4. BUILD DOCKER =========="

  docker compose \
    -f compose.yaml \
    -f compose.local.yaml \
    -f compose.https.yaml \
    up -d --build api local-https

  echo "========== 5. CONTAINER STATUS =========="

  docker ps --format \
    "table {{.Names}}\t{{.Status}}\t{{.Ports}}"

  echo "========== 6. HTTPS CHECK =========="

  curl -ksS \
    --max-time 15 \
    -o /dev/null \
    -w 'HTTP %{http_code}\n' \
    https://localhost:7298/api/v1/security/antiforgery

  echo "========== READY =========="
)