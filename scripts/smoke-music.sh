#!/usr/bin/env bash
set -euo pipefail

BASE_URL="${BASE_URL:-https://localhost:7298}"

if ! command -v curl >/dev/null 2>&1; then
    echo "curl is required."
    exit 1
fi

if ! command -v jq >/dev/null 2>&1; then
    echo "jq is required. On macOS: brew install jq"
    exit 1
fi

case "$BASE_URL" in
    https://localhost:*|https://127.0.0.1:*)
        ;;
    *)
        echo "This smoke test supports local HTTPS only."
        exit 1
        ;;
esac

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

COOKIE_JAR="$WORK_DIR/cookies.txt"
RESPONSE_BODY="$WORK_DIR/response.json"

touch "$COOKIE_JAR"
chmod 600 "$COOKIE_JAR"

check_status() {
    local actual="$1"
    local expected="$2"
    local scenario="$3"

    if [[ "$actual" != "$expected" ]]; then
        echo "FAIL: $scenario"
        echo "Expected HTTP $expected, received HTTP $actual"

        jq . "$RESPONSE_BODY" 2>/dev/null \
            || cat "$RESPONSE_BODY"

        exit 1
    fi

    echo "PASS: $scenario (HTTP $actual)"
}

echo "=== VibyRoom Audius Smoke Test ==="

# 1. Authentication protection.
status="$(curl -ksS \
    -o "$RESPONSE_BODY" \
    -w "%{http_code}" \
    "$BASE_URL/api/v1/music/tracks/search?query=electronic")"

check_status "$status" "401" "Guest authentication"

# 2. Retrieve antiforgery token.
status="$(curl -ksS \
    --cookie "$COOKIE_JAR" \
    --cookie-jar "$COOKIE_JAR" \
    -o "$RESPONSE_BODY" \
    -w "%{http_code}" \
    "$BASE_URL/api/v1/security/antiforgery")"

check_status "$status" "200" "Antiforgery token"

CSRF_TOKEN="$(jq -er '.requestToken' "$RESPONSE_BODY")"
CSRF_HEADER="$(jq -er '.headerName' "$RESPONSE_BODY")"

# 3. Create authenticated guest session.
status="$(curl -ksS \
    --request POST \
    --cookie "$COOKIE_JAR" \
    --cookie-jar "$COOKIE_JAR" \
    --header "$CSRF_HEADER: $CSRF_TOKEN" \
    -o "$RESPONSE_BODY" \
    -w "%{http_code}" \
    "$BASE_URL/api/v1/guest-sessions")"

check_status "$status" "200" "Guest session"

# 4. Search real Audius catalog.
status="$(curl -ksS \
    --get \
    --cookie "$COOKIE_JAR" \
    --cookie-jar "$COOKIE_JAR" \
    --data-urlencode "query=electronic" \
    --data-urlencode "limit=5" \
    -o "$RESPONSE_BODY" \
    -w "%{http_code}" \
    "$BASE_URL/api/v1/music/tracks/search")"

check_status "$status" "200" "Music search"

jq -e '.tracks | type == "array"' \
    "$RESPONSE_BODY" >/dev/null

echo
echo "Search results:"

jq '[.tracks[] | {
    reference,
    title,
    artists,
    durationMs
}]' "$RESPONSE_BODY"

TRACK_ID="$(jq -r \
    '.tracks[0].reference.trackId // empty' \
    "$RESPONSE_BODY")"

if [[ -z "$TRACK_ID" ]]; then
    echo "Search returned no tracks."
    echo "Try another search query before testing details."
    exit 1
fi

# 5. Get details for the same track.
status="$(curl -ksS \
    --get \
    --cookie "$COOKIE_JAR" \
    --cookie-jar "$COOKIE_JAR" \
    --data-urlencode "provider=audius" \
    --data-urlencode "trackId=$TRACK_ID" \
    -o "$RESPONSE_BODY" \
    -w "%{http_code}" \
    "$BASE_URL/api/v1/music/tracks/details")"

check_status "$status" "200" "Track details"

jq -e --arg id "$TRACK_ID" '
    .reference.provider == "audius"
    and .reference.trackId == $id
    and (.title | type == "string")
' "$RESPONSE_BODY" >/dev/null

echo
echo "Track details:"

jq '{
    reference,
    title,
    artists,
    album,
    durationMs,
    artworkUrl,
    externalUrl
}' "$RESPONSE_BODY"

# 6. Reject invalid track ID.
status="$(curl -ksS \
    --get \
    --cookie "$COOKIE_JAR" \
    --cookie-jar "$COOKIE_JAR" \
    --data-urlencode "provider=audius" \
    --data-urlencode "trackId=invalid/id" \
    -o "$RESPONSE_BODY" \
    -w "%{http_code}" \
    "$BASE_URL/api/v1/music/tracks/details")"

check_status "$status" "400" "Invalid track ID"

echo
echo "All Audius smoke checks passed."