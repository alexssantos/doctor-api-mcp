#!/usr/bin/env bash
set -Eeuo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
CHART="${REPO_ROOT}/infra/helm/doctor-api-mcp"
SCENARIO=""
NAMESPACE="mcp-install-test"
RELEASE="doctor-api-mcp"
CONTEXT=""
CREATE_CLUSTER=false
CLUSTER_NAME=""
K3S_IMAGE="rancher/k3s:v1.36.1-k3s1"
IMAGE_REPOSITORY="doctor-api-mcp-test"
IMAGE_TAG="local"
BUILD_IMAGE=false
PRESERVE=false
PRESERVE_ON_FAILURE=false
LOAD_PROFILE=""
PF_PID=""
CREATED_NAMESPACE=false
CREATED_CLUSTER=false
RUN_ID="$(date -u +%Y%m%dT%H%M%SZ)-${RANDOM}"
REPORT_DIR=""

usage() {
  cat <<'EOF'
Usage: run-installation-scenario.sh --scenario NAME [options]

Scenarios: cluster, namespace-only, no-volumes, no-service-discovery, restricted

  --namespace NAME
  --release NAME
  --context KUBECTL_CONTEXT
  --create-cluster NAME
  --build-image
  --image-repository NAME
  --image-tag TAG
  --load-profile smoke|average|spike|soak
  --preserve
  --preserve-on-failure
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --scenario) SCENARIO="$2"; shift 2 ;;
    --namespace) NAMESPACE="$2"; shift 2 ;;
    --release) RELEASE="$2"; shift 2 ;;
    --context) CONTEXT="$2"; shift 2 ;;
    --create-cluster) CREATE_CLUSTER=true; CLUSTER_NAME="$2"; shift 2 ;;
    --build-image) BUILD_IMAGE=true; shift ;;
    --image-repository) IMAGE_REPOSITORY="$2"; shift 2 ;;
    --image-tag) IMAGE_TAG="$2"; shift 2 ;;
    --load-profile) LOAD_PROFILE="$2"; shift 2 ;;
    --preserve) PRESERVE=true; shift ;;
    --preserve-on-failure) PRESERVE_ON_FAILURE=true; shift ;;
    -h|--help) usage; exit 0 ;;
    *) printf '[FAIL] Unknown argument: %s\n' "$1" >&2; usage; exit 2 ;;
  esac
done

if [[ ! "${SCENARIO}" =~ ^(cluster|namespace-only|no-volumes|no-service-discovery|restricted)$ ]]; then
  printf '[FAIL] --scenario must name a supported scenario.\n' >&2
  usage
  exit 2
fi
if [[ ! "${NAMESPACE}" =~ ^mcp-(install|load)-[a-z0-9]([a-z0-9-]*[a-z0-9])?$ ]]; then
  printf '[FAIL] Test namespaces must use the mcp-install-* or mcp-load-* prefix: %s\n' \
    "${NAMESPACE}" >&2
  exit 2
fi
if [[ "${CREATE_CLUSTER}" == "true" && ! "${CLUSTER_NAME}" =~ ^mcp-test-[a-z0-9-]+$ ]]; then
  printf '[FAIL] Test clusters must use the mcp-test-* prefix.\n' >&2
  exit 2
fi

case "${SCENARIO}" in
  cluster)
    SCOPE=Cluster; SERVICE_DISCOVERY=true; STATE_STORAGE=ConfigMap
    DEPLOYMENT_EVENTS=true; PDB=true; EXPECTED_MODE=cluster
    EXPECTED_PRESENT=(list_services get_health find_data_origin)
    EXPECTED_ABSENT=()
    ;;
  namespace-only)
    SCOPE=Namespace; SERVICE_DISCOVERY=true; STATE_STORAGE=ConfigMap
    DEPLOYMENT_EVENTS=true; PDB=true; EXPECTED_MODE=namespace-only
    EXPECTED_PRESENT=(list_services get_health find_data_origin)
    EXPECTED_ABSENT=()
    ;;
  no-volumes)
    SCOPE=Cluster; SERVICE_DISCOVERY=true; STATE_STORAGE=ConfigMap
    DEPLOYMENT_EVENTS=true; PDB=true; EXPECTED_MODE=no-volumes
    EXPECTED_PRESENT=(list_services get_health find_data_origin)
    EXPECTED_ABSENT=()
    ;;
  no-service-discovery)
    SCOPE=Namespace; SERVICE_DISCOVERY=false; STATE_STORAGE=ConfigMap
    DEPLOYMENT_EVENTS=true; PDB=true; EXPECTED_MODE=no-service-discovery
    EXPECTED_PRESENT=(get_health find_data_origin)
    EXPECTED_ABSENT=(list_services)
    ;;
  restricted)
    SCOPE=None; SERVICE_DISCOVERY=false; STATE_STORAGE=Memory
    DEPLOYMENT_EVENTS=false; PDB=false; EXPECTED_MODE=restricted
    EXPECTED_PRESENT=()
    EXPECTED_ABSENT=(list_services get_health find_data_origin)
    ;;
esac

REPORT_DIR="${REPO_ROOT}/tests/cluster-lab/reports/${RUN_ID}-${SCENARIO}"
mkdir -p "${REPORT_DIR}"

KUBECTL=(kubectl)
HELM=(helm)

cleanup() {
  local exit_code=$?
  [[ -n "${PF_PID}" ]] && kill "${PF_PID}" >/dev/null 2>&1 || true

  local keep=false
  [[ "${PRESERVE}" == "true" ]] && keep=true
  [[ "${exit_code}" -ne 0 && "${PRESERVE_ON_FAILURE}" == "true" ]] && keep=true
  if [[ "${keep}" == "true" ]]; then
    printf '[INFO] Preserving test resources for diagnosis (context=%s namespace=%s).\n' \
      "${CONTEXT:-current}" "${NAMESPACE}"
    return
  fi

  if [[ "${CREATED_NAMESPACE}" == "true" ]]; then
    local owner
    owner="$("${KUBECTL[@]}" get namespace "${NAMESPACE}" \
      -o jsonpath='{.metadata.labels.doctor-api-mcp-test-run}' 2>/dev/null || true)"
    if [[ "${owner}" == "${RUN_ID}" ]]; then
      "${HELM[@]}" uninstall "${RELEASE}" -n "${NAMESPACE}" >/dev/null 2>&1 || true
      "${KUBECTL[@]}" delete namespace "${NAMESPACE}" --wait=false >/dev/null 2>&1 || true
    else
      printf '[WARN] Namespace ownership changed; cleanup skipped: %s\n' "${NAMESPACE}" >&2
    fi
  fi

  if [[ "${CREATED_CLUSTER}" == "true" && "${CLUSTER_NAME}" =~ ^mcp-test-[a-z0-9-]+$ ]]; then
    k3d cluster delete "${CLUSTER_NAME}" >/dev/null 2>&1 || true
  fi
}
trap cleanup EXIT

for command_name in docker helm kubectl curl; do
  command -v "${command_name}" >/dev/null 2>&1 || {
    printf '[FAIL] Missing command: %s\n' "${command_name}" >&2
    exit 1
  }
done
[[ "${CREATE_CLUSTER}" == "false" ]] || command -v k3d >/dev/null 2>&1 || {
  printf '[FAIL] Missing command: k3d\n' >&2
  exit 1
}

if [[ "${BUILD_IMAGE}" == "true" ]]; then
  docker build -f "${REPO_ROOT}/src/Services/McpServer/Dockerfile" \
    -t "${IMAGE_REPOSITORY}:${IMAGE_TAG}" "${REPO_ROOT}"
fi

if [[ "${CREATE_CLUSTER}" == "true" ]]; then
  CREATED_CLUSTER=true
  k3d cluster create "${CLUSTER_NAME}" --image "${K3S_IMAGE}" --agents 1 --wait
  CONTEXT="k3d-${CLUSTER_NAME}"
  k3d image import "${IMAGE_REPOSITORY}:${IMAGE_TAG}" --cluster "${CLUSTER_NAME}"
fi

if [[ -n "${CONTEXT}" ]]; then
  KUBECTL+=(--context "${CONTEXT}")
  HELM+=(--kube-context "${CONTEXT}")
fi

"${KUBECTL[@]}" get nodes -o wide >"${REPORT_DIR}/nodes.txt"

PREFLIGHT_CONTEXT_ARGS=()
[[ -n "${CONTEXT}" ]] && PREFLIGHT_CONTEXT_ARGS=(--context "${CONTEXT}")
bash "${REPO_ROOT}/infra/scripts/sh/validate-install-requirements.sh" \
  --phase installer --namespace "${NAMESPACE}" --release "${RELEASE}" \
  --scope "${SCOPE}" --service-discovery "${SERVICE_DISCOVERY}" \
  --state-storage "${STATE_STORAGE}" --deployment-events "${DEPLOYMENT_EVENTS}" \
  --pdb "${PDB}" "${PREFLIGHT_CONTEXT_ARGS[@]}" \
  | tee "${REPORT_DIR}/installer-requirements.txt"

if "${KUBECTL[@]}" get namespace "${NAMESPACE}" >/dev/null 2>&1; then
  PREVIOUS_OWNER="$("${KUBECTL[@]}" get namespace "${NAMESPACE}" \
    -o jsonpath='{.metadata.labels.doctor-api-mcp-test-run}' 2>/dev/null || true)"
  if [[ -z "${PREVIOUS_OWNER}" ]]; then
    printf '[FAIL] Refusing to reuse an existing namespace not owned by cluster-lab: %s\n' \
      "${NAMESPACE}" >&2
    exit 1
  fi
else
  "${KUBECTL[@]}" create namespace "${NAMESPACE}"
fi
"${KUBECTL[@]}" label namespace "${NAMESPACE}" \
  "doctor-api-mcp-test-run=${RUN_ID}" --overwrite >/dev/null
CREATED_NAMESPACE=true

"${HELM[@]}" upgrade --install "${RELEASE}" "${CHART}" \
  --namespace "${NAMESPACE}" \
  -f "${REPO_ROOT}/tests/cluster-lab/scenarios/${SCENARIO}.yaml" \
  --set-string security.allowedNamespaces[0]="${NAMESPACE}" \
  --set-string services.fixture="http://fixture.${NAMESPACE}.svc.cluster.local" \
  --set-string image.repository="${IMAGE_REPOSITORY}" \
  --set-string image.tag="${IMAGE_TAG}" \
  --set image.pullPolicy=Never \
  --wait --timeout 5m

"${HELM[@]}" get values "${RELEASE}" -n "${NAMESPACE}" -a \
  >"${REPORT_DIR}/helm-values.yaml"
"${HELM[@]}" get manifest "${RELEASE}" -n "${NAMESPACE}" \
  >"${REPORT_DIR}/helm-manifest.yaml"
"${KUBECTL[@]}" get all,configmap,role,rolebinding -n "${NAMESPACE}" -o wide \
  >"${REPORT_DIR}/namespace-resources.txt" 2>&1 || true

bash "${REPO_ROOT}/infra/scripts/sh/validate-install-requirements.sh" \
  --phase runtime --namespace "${NAMESPACE}" --release "${RELEASE}" \
  --scope "${SCOPE}" --service-discovery "${SERVICE_DISCOVERY}" \
  --state-storage "${STATE_STORAGE}" --deployment-events "${DEPLOYMENT_EVENTS}" \
  "${PREFLIGHT_CONTEXT_ARGS[@]}" \
  | tee "${REPORT_DIR}/runtime-requirements.txt"

"${HELM[@]}" test "${RELEASE}" -n "${NAMESPACE}" --timeout 2m \
  | tee "${REPORT_DIR}/helm-test.txt"

LOCAL_PORT=$((24000 + RANDOM % 10000))
SERVICE_NAME="$("${KUBECTL[@]}" get service -n "${NAMESPACE}" \
  -l "app.kubernetes.io/instance=${RELEASE}" \
  -o jsonpath='{.items[0].metadata.name}')"
[[ -n "${SERVICE_NAME}" ]]
"${KUBECTL[@]}" port-forward -n "${NAMESPACE}" "service/${SERVICE_NAME}" \
  "${LOCAL_PORT}:4000" >"${REPORT_DIR}/port-forward.log" 2>&1 &
PF_PID=$!
for _ in {1..30}; do
  curl -fsS --max-time 2 "http://127.0.0.1:${LOCAL_PORT}/ready" \
    >"${REPORT_DIR}/ready.json" 2>/dev/null && break
  sleep 1
done
grep -q '"status":"ready"' "${REPORT_DIR}/ready.json"

curl -fsS --max-time 10 "http://127.0.0.1:${LOCAL_PORT}/api/requirements?refresh=true" \
  >"${REPORT_DIR}/requirements.json"
grep -q '"meetsMinimumRequirements":true' "${REPORT_DIR}/requirements.json"
grep -q "\"mode\":\"${EXPECTED_MODE}\"" "${REPORT_DIR}/requirements.json"

curl -sS --max-time 15 -D "${REPORT_DIR}/mcp-headers.txt" \
  -c "${REPORT_DIR}/mcp-cookies.txt" \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"cluster-lab","version":"1.0"}}}' \
  "http://127.0.0.1:${LOCAL_PORT}/" >"${REPORT_DIR}/mcp-initialize.txt"
SESSION_ID="$(awk 'BEGIN{IGNORECASE=1} /^Mcp-Session-Id:/ {gsub("\r", "", $2); print $2}' \
  "${REPORT_DIR}/mcp-headers.txt" | tail -1)"
[[ -n "${SESSION_ID}" ]]

curl -sS --max-time 10 -b "${REPORT_DIR}/mcp-cookies.txt" \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -H "Mcp-Session-Id: ${SESSION_ID}" \
  -d '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
  "http://127.0.0.1:${LOCAL_PORT}/" >/dev/null
curl -sS --max-time 15 -b "${REPORT_DIR}/mcp-cookies.txt" \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -H "Mcp-Session-Id: ${SESSION_ID}" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' \
  "http://127.0.0.1:${LOCAL_PORT}/" >"${REPORT_DIR}/mcp-tools.txt"

curl -sS --max-time 40 -b "${REPORT_DIR}/mcp-cookies.txt" \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -H "Mcp-Session-Id: ${SESSION_ID}" \
  -d '{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"system_get_health_summary","arguments":{"windowMinutes":5}}}' \
  "http://127.0.0.1:${LOCAL_PORT}/" >"${REPORT_DIR}/mcp-system-health.txt"
grep -q 'schemaVersion' "${REPORT_DIR}/mcp-system-health.txt" || {
  printf '[FAIL] system_get_health_summary did not return a versioned envelope.\n' >&2
  exit 1
}

CORE_TOOLS=(
  list_discovered_applications get_openapi trace_route explain_api find_dependencies
  service_get_spec service_get_health service_get_score service_get_dependencies
  service_detect_anomalies service_get_incident_timeline service_find_root_cause
  system_get_health_summary
)
for tool in "${CORE_TOOLS[@]}" "${EXPECTED_PRESENT[@]}"; do
  grep -q "\"${tool}\"" "${REPORT_DIR}/mcp-tools.txt" || {
    printf '[FAIL] Missing MCP tool %s in scenario %s.\n' "${tool}" "${SCENARIO}" >&2
    exit 1
  }
done
for tool in "${EXPECTED_ABSENT[@]}" query_metrics; do
  if grep -q "\"${tool}\"" "${REPORT_DIR}/mcp-tools.txt"; then
    printf '[FAIL] Unexpected MCP tool %s in scenario %s.\n' "${tool}" "${SCENARIO}" >&2
    exit 1
  fi
done

if [[ -n "${LOAD_PROFILE}" ]]; then
  bash "${REPO_ROOT}/tests/cluster-lab/scripts/run-load.sh" \
    --base-url "http://127.0.0.1:${LOCAL_PORT}" \
    --profile "${LOAD_PROFILE}" \
    --output "${REPORT_DIR}/k6-summary.json"
fi

"${KUBECTL[@]}" get pods -n "${NAMESPACE}" -o wide >"${REPORT_DIR}/pods.txt"
"${KUBECTL[@]}" get events -n "${NAMESPACE}" --sort-by=.lastTimestamp \
  >"${REPORT_DIR}/events.txt" 2>&1 || true
printf 'SCENARIO_RESULT:scenario=%s:status=pass:report=%s\n' "${SCENARIO}" "${REPORT_DIR}"
