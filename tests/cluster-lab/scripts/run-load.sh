#!/usr/bin/env bash
set -Eeuo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
BASE_URL="http://127.0.0.1:4000"
PROFILE="smoke"
OUTPUT="${REPO_ROOT}/tests/cluster-lab/reports/k6-summary.json"
REQUEST_RATE="${REQUEST_RATE:-5}"
P95_MS="${P95_MS:-2000}"
MAX_ERROR_RATE="${MAX_ERROR_RATE:-0.01}"

while [[ $# -gt 0 ]]; do
  case "$1" in
    --base-url) BASE_URL="$2"; shift 2 ;;
    --profile) PROFILE="$2"; shift 2 ;;
    --output) OUTPUT="$2"; shift 2 ;;
    *) printf '[FAIL] Unknown argument: %s\n' "$1" >&2; exit 2 ;;
  esac
done

command -v k6 >/dev/null 2>&1 || {
  printf '[FAIL] k6 is required for volumetry.\n' >&2
  exit 1
}
[[ "${PROFILE}" =~ ^(smoke|average|spike|soak)$ ]] || {
  printf '[FAIL] Invalid load profile: %s\n' "${PROFILE}" >&2
  exit 2
}

mkdir -p "$(dirname "${OUTPUT}")"
BASE_URL="${BASE_URL}" LOAD_PROFILE="${PROFILE}" REQUEST_RATE="${REQUEST_RATE}" \
  P95_MS="${P95_MS}" MAX_ERROR_RATE="${MAX_ERROR_RATE}" \
  k6 run --summary-export "${OUTPUT}" \
  "${REPO_ROOT}/tests/cluster-lab/load/mcp-load.js"
