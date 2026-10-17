(
  set -e

  BASE="https://localhost:7298"
  TMP=$(mktemp -d)
  COOKIE="$TMP/cookies.txt"

  touch "$COOKIE"

  echo "Test files: $TMP"

  request() {
    OUTPUT="$1"
    shift

    STATUS=$(curl -ksS \
      --max-time 360 \
      -b "$COOKIE" \
      -c "$COOKIE" \
      -o "$OUTPUT" \
      -w '%{http_code}' \
      "$@")

    echo "HTTP $STATUS"

    case "$STATUS" in
      2??)
        ;;
      *)
        echo "========== ERROR RESPONSE =========="
        cat "$OUTPUT"
        echo
        echo "Saved files: $TMP"
        exit 1
        ;;
    esac
  }

  echo "========== 1. CSRF =========="

  request "$TMP/csrf.json" \
    "$BASE/api/v1/security/antiforgery"

  CSRF=$(python3 -c '
import json, sys
print(json.load(open(sys.argv[1]))["requestToken"])
' "$TMP/csrf.json")

  echo "========== 2. GUEST SESSION =========="

  request "$TMP/guest.json" \
    -X POST \
    -H "X-CSRF-TOKEN: $CSRF" \
    "$BASE/api/v1/guest-sessions"

  echo "========== 3. REFRESH CSRF =========="

  request "$TMP/csrf.json" \
    "$BASE/api/v1/security/antiforgery"

  CSRF=$(python3 -c '
import json, sys
print(json.load(open(sys.argv[1]))["requestToken"])
' "$TMP/csrf.json")

  echo "========== 4. CREATE ROOM =========="

  request "$TMP/room.json" \
    -X POST \
    -H "X-CSRF-TOKEN: $CSRF" \
    -H "Content-Type: application/json" \
    -d '{"name":"AI DJ Test","displayName":"Owner"}' \
    "$BASE/api/v1/rooms"

  ROOM_ID=$(python3 -c '
import json, sys
print(json.load(open(sys.argv[1]))["id"])
' "$TMP/room.json")

  echo "Room ID: $ROOM_ID"

  echo "========== 5. AI GENERATE 15 =========="

  request "$TMP/generate.json" \
    -X POST \
    -H "X-CSRF-TOKEN: $CSRF" \
    -H "Content-Type: application/json" \
    -d '{
      "prompt": "Энергичная электронная музыка, synthwave, retrowave, ночная поездка по городу"
    }' \
    "$BASE/api/v1/rooms/$ROOM_ID/ai/generate"

  python3 - "$TMP/generate.json" <<'PY'
import json
import sys

with open(sys.argv[1]) as f:
    data = json.load(f)

queue = data["queue"]

print("Generated tracks:", len(queue))

assert len(queue) == 15, \
    f"Expected 15 tracks, got {len(queue)}"

for index, item in enumerate(queue, 1):
    track = item["track"]
    artists = ", ".join(track.get("artists") or [])
    print(f"{index:02d}. {artists} - {track['title']}")

print("GENERATE 15 PASSED")
PY

  echo "========== 6. AI MORE 15 =========="

  request "$TMP/more.json" \
    -X POST \
    -H "X-CSRF-TOKEN: $CSRF" \
    "$BASE/api/v1/rooms/$ROOM_ID/ai/more"

  echo "========== 7. AI CONTEXT =========="

  request "$TMP/context.json" \
    "$BASE/api/v1/rooms/$ROOM_ID/ai"

  python3 -m json.tool "$TMP/context.json"

  echo "========== 8. FINAL QUEUE =========="

  request "$TMP/queue.json" \
    "$BASE/api/v1/rooms/$ROOM_ID/queue"

  echo "========== 9. VALIDATE 30 TRACKS =========="

  python3 - "$TMP/queue.json" <<'PY'
import json
import sys

with open(sys.argv[1]) as f:
    data = json.load(f)

queue = data["queue"]

ids = [
    item["track"]["reference"]["trackId"]
    for item in queue
]

unique_ids = set(ids)

print("Total tracks:", len(ids))
print("Unique tracks:", len(unique_ids))

assert len(ids) == 30, \
    f"Expected 30 tracks, got {len(ids)}"

assert len(unique_ids) == 30, \
    "Duplicate Audius track IDs found"

print()
print("========== TRACK LIST ==========")

for index, item in enumerate(queue, 1):
    track = item["track"]
    artists = ", ".join(track.get("artists") or [])
    print(f"{index:02d}. {artists} - {track['title']}")

print()
print("AI DJ GENERATE + MORE PASSED")
PY

  echo "========== TEST COMPLETED =========="

  echo "Room ID: $ROOM_ID"
  echo "Test files: $TMP"
)