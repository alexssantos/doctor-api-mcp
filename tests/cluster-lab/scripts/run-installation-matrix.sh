#!/usr/bin/env bash
set -Eeuo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
CONTEXT=""
BUILD_IMAGE=false
PRESERVE_ON_FAILURE=false

while [[ $# -gt 0 ]]; do
  case "$1" in
    --context) CONTEXT="$2"; shift 2 ;;
    --build-image) BUILD_IMAGE=true; shift ;;
    --preserve-on-failure) PRESERVE_ON_FAILURE=true; shift ;;
    *) printf '[FAIL] Unknown argument: %s\n' "$1" >&2; exit 2 ;;
  esac
done

if [[ -z "${CONTEXT}" ]]; then
  printf '[FAIL] --context is required. Use a dedicated k3d test context.\n' >&2
  exit 2
fi
if [[ ! "${CONTEXT}" =~ ^k3d-mcp-test-[a-z0-9-]+$ ]]; then
  printf '[FAIL] --context must target a dedicated k3d-mcp-test-* cluster.\n' >&2
  exit 2
fi

if [[ "${BUILD_IMAGE}" == "true" ]]; then
  docker build -f "${REPO_ROOT}/src/Services/McpServer/Dockerfile" \
    -t doctor-api-mcp-test:local "${REPO_ROOT}"
  cluster_name="${CONTEXT#k3d-}"
  k3d image import doctor-api-mcp-test:local --cluster "${cluster_name}"
fi

for scenario in cluster namespace-only no-volumes no-service-discovery restricted; do
  namespace="mcp-install-${scenario}"
  args=(
    --scenario "${scenario}"
    --namespace "${namespace}"
    --context "${CONTEXT}"
    --image-repository doctor-api-mcp-test
    --image-tag local
  )
  [[ "${PRESERVE_ON_FAILURE}" == "true" ]] && args+=(--preserve-on-failure)
  bash "${REPO_ROOT}/tests/cluster-lab/scripts/run-installation-scenario.sh" "${args[@]}"
done

printf 'INSTALLATION_MATRIX_RESULT:status=pass\n'
