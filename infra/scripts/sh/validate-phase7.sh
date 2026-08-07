#!/usr/bin/env bash
# Validates the Phase 7 k3d release gates. Use --resilience to perform a
# rolling restart and an active cross-namespace NetworkPolicy denial test.
set -uo pipefail

export PATH="$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin"

NAMESPACE="${PHASE7_NAMESPACE:-mcp-apis}"
CONTEXT="${PHASE7_CONTEXT:-k3d-mcp-apis}"
INGRESS_BASE_URL="${PHASE7_INGRESS_URL:-http://127.0.0.1:8080}"
SERVICE_ACCOUNT="system:serviceaccount:${NAMESPACE}:mcp-reader"
REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
RUN_RESILIENCE=false
[[ "${1:-}" == "--resilience" ]] && RUN_RESILIENCE=true

PASS=0
FAIL=0
PF_PIDS=()
TEST_NAMESPACE="mcp-apis-phase7-denied"
WORK_DIR="$(mktemp -d)"

pass() { printf '  [PASS] %s\n' "$1"; PASS=$((PASS + 1)); }
fail() { printf '  [FAIL] %s\n' "$1"; FAIL=$((FAIL + 1)); }
section() { printf '\n=== %s ===\n' "$1"; }

stop_port_forwards() {
  if [[ ${#PF_PIDS[@]} -gt 0 ]]; then
    kill "${PF_PIDS[@]}" 2>/dev/null || true
    wait "${PF_PIDS[@]}" 2>/dev/null || true
    PF_PIDS=()
  fi
}

cleanup() {
  stop_port_forwards
  kubectl delete namespace "$TEST_NAMESPACE" --ignore-not-found --wait=false >/dev/null 2>&1 || true
  rm -rf -- "$WORK_DIR"
}
trap cleanup EXIT

start_port_forwards() {
  stop_port_forwards
  kubectl port-forward -n "$NAMESPACE" svc/mcpserver 14000:4000 >"$WORK_DIR/mcp-pf.log" 2>&1 &
  PF_PIDS+=($!)
  kubectl port-forward -n "$NAMESPACE" svc/prometheus 19090:9090 >"$WORK_DIR/prom-pf.log" 2>&1 &
  PF_PIDS+=($!)
  kubectl port-forward -n "$NAMESPACE" svc/grafana 13000:3000 >"$WORK_DIR/grafana-pf.log" 2>&1 &
  PF_PIDS+=($!)

  for _ in {1..20}; do
    if curl -fsS --max-time 2 http://127.0.0.1:14000/live >/dev/null 2>&1 &&
       curl -fsS --max-time 2 http://127.0.0.1:19090/-/ready >/dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done
  return 1
}

expect_can_i() {
  local expected="$1" label="$2"
  shift 2
  local actual
  actual="$(kubectl auth can-i --as="$SERVICE_ACCOUNT" "$@" 2>/dev/null || true)"
  if [[ "$actual" == "$expected" ]]; then
    pass "$label -> $expected"
  else
    fail "$label -> expected $expected, got ${actual:-no-result}"
  fi
}

expect_http() {
  local label="$1" url="$2" expected="$3"
  local actual
  actual="$(curl -sS -o /dev/null -w '%{http_code}' --max-time 15 "$url" 2>/dev/null || true)"
  if [[ "$actual" == "$expected" ]]; then
    pass "$label -> HTTP $actual"
  else
    fail "$label -> expected HTTP $expected, got ${actual:-000}"
  fi
}

section "Context and rendered manifests"
if kubectl config use-context "$CONTEXT" >/dev/null 2>&1; then
  pass "kubectl context $CONTEXT"
else
  fail "kubectl context $CONTEXT is unavailable"
fi

KUBELET_VERSION="$(kubectl get node -o jsonpath='{.items[0].status.nodeInfo.kubeletVersion}' 2>/dev/null || true)"
if [[ "$KUBELET_VERSION" =~ ^v([0-9]+)\.([0-9]+) ]] &&
   (( BASH_REMATCH[1] > 1 || (BASH_REMATCH[1] == 1 && BASH_REMATCH[2] >= 36) )); then
  pass "Kubernetes runtime ${KUBELET_VERSION} meets the validated NetworkPolicy floor"
else
  fail "Kubernetes runtime ${KUBELET_VERSION:-unknown} is below the validated K3s 1.36.1 floor"
fi

TRAEFIK_READY="$(kubectl get deployment traefik -n kube-system -o jsonpath='{.status.readyReplicas}' 2>/dev/null || true)"
INGRESS_CLASS="$(kubectl get ingress mcpserver-ingress -n "$NAMESPACE" -o jsonpath='{.spec.ingressClassName}' 2>/dev/null || true)"
if [[ "${TRAEFIK_READY:-0}" -ge 1 && "$INGRESS_CLASS" == "traefik" ]]; then
  pass "K3s built-in Traefik is ready and owns the MCP Ingress"
else
  fail "Traefik gate ready=${TRAEFIK_READY:-0} ingressClass=${INGRESS_CLASS:-missing}"
fi

if kubectl kustomize "$REPO_ROOT/infra/k8s/overlays/k3d" >"$WORK_DIR/mcpserver.yaml" &&
   kubectl apply --dry-run=server -f "$WORK_DIR/mcpserver.yaml" >/dev/null 2>&1; then
  pass "k3d overlay renders and passes server-side dry-run"
else
  fail "k3d overlay render/server-side dry-run"
fi

section "Availability and rollout configuration"
READY_REPLICAS="$(kubectl get deployment mcpserver -n "$NAMESPACE" -o jsonpath='{.status.readyReplicas}' 2>/dev/null || true)"
DESIRED_REPLICAS="$(kubectl get deployment mcpserver -n "$NAMESPACE" -o jsonpath='{.spec.replicas}' 2>/dev/null || true)"
if [[ "${READY_REPLICAS:-0}" -ge 2 && "$READY_REPLICAS" == "$DESIRED_REPLICAS" ]]; then
  pass "mcpserver has $READY_REPLICAS/$DESIRED_REPLICAS ready replicas"
else
  fail "mcpserver ready replicas (${READY_REPLICAS:-0}/${DESIRED_REPLICAS:-0})"
fi

READINESS_PATH="$(kubectl get deployment mcpserver -n "$NAMESPACE" -o jsonpath='{.spec.template.spec.containers[0].readinessProbe.httpGet.path}' 2>/dev/null || true)"
LIVENESS_PATH="$(kubectl get deployment mcpserver -n "$NAMESPACE" -o jsonpath='{.spec.template.spec.containers[0].livenessProbe.httpGet.path}' 2>/dev/null || true)"
[[ "$READINESS_PATH" == "/ready" && "$LIVENESS_PATH" == "/live" ]] &&
  pass "readiness and liveness probes are separated" ||
  fail "probe paths are readiness=${READINESS_PATH:-missing}, liveness=${LIVENESS_PATH:-missing}"

kubectl get poddisruptionbudget mcpserver -n "$NAMESPACE" >/dev/null 2>&1 &&
  pass "PodDisruptionBudget exists" || fail "PodDisruptionBudget is missing"
kubectl get networkpolicy mcpserver -n "$NAMESPACE" >/dev/null 2>&1 &&
  pass "NetworkPolicy exists" || fail "NetworkPolicy is missing"

section "Effective RBAC matrix"
expect_can_i yes "read pods cluster-wide" get pods --all-namespaces
expect_can_i yes "read deployments cluster-wide" list deployments.apps --all-namespaces
expect_can_i yes "read Kubernetes events" list events -n "$NAMESPACE"
expect_can_i yes "persist own administrative state" patch configmap/mcpserver-state -n "$NAMESPACE"
expect_can_i no "cannot patch runtime configuration" patch configmap/mcpserver-config -n "$NAMESPACE"
expect_can_i no "cannot read Secrets" get secrets -n "$NAMESPACE"
expect_can_i no "cannot delete Pods" delete pods -n "$NAMESPACE"

section "HTTP contracts, cache and release telemetry"
if start_port_forwards; then
  pass "validation port-forwards started"
else
  fail "validation port-forwards did not become ready"
fi

expect_http "liveness" "http://127.0.0.1:14000/live" 200
expect_http "readiness" "http://127.0.0.1:14000/ready" 200
expect_http "dashboard shell" "http://127.0.0.1:14000/dashboard/" 200
expect_http "system overview" "http://127.0.0.1:14000/api/dashboard/overview" 200
expect_http "system intelligence" "http://127.0.0.1:14000/api/dashboard/intelligence/system?minutes=30" 200
expect_http "bounded invalid window" "http://127.0.0.1:14000/api/dashboard/intelligence/system?minutes=99999" 400
expect_http "raw PromQL remains disabled" "http://127.0.0.1:14000/api/dashboard/admin/metrics?query=up" 404

OVERVIEW_BEFORE="$(curl -fsS --max-time 30 http://127.0.0.1:14000/api/dashboard/overview 2>/dev/null || true)"
curl -fsS --max-time 30 http://127.0.0.1:14000/api/dashboard/overview >/dev/null 2>&1 || true
if echo "$OVERVIEW_BEFORE" | grep -q '"generatedAt"' &&
   echo "$OVERVIEW_BEFORE" | grep -q '"system"' &&
   echo "$OVERVIEW_BEFORE" | grep -q '"sources"'; then
  pass "overview preserves generatedAt/system/sources contract"
else
  fail "overview response contract"
fi

METRICS="$(curl -fsS --max-time 10 http://127.0.0.1:14000/metrics 2>/dev/null || true)"
echo "$METRICS" | grep -q 'mcp_observability_cache_requests_total' &&
  pass "cache hit/miss telemetry is exposed" || fail "cache telemetry is missing"
echo "$METRICS" | grep -q 'mcp_observability_provider_calls_total' &&
  pass "provider outcome telemetry is exposed" || fail "provider telemetry is missing"

PROM_RULES="$(curl -fsS --max-time 10 http://127.0.0.1:19090/api/v1/rules 2>/dev/null || true)"
echo "$PROM_RULES" | grep -q 'McpServerUnavailable' &&
  pass "Prometheus MCP alerts are loaded" || fail "Prometheus MCP alerts are not loaded"
echo "$PROM_RULES" | grep -q 'mcpserver:slo_availability:ratio5m' &&
  pass "Prometheus SLO recording rules are loaded" || fail "Prometheus SLO rules are not loaded"

GRAFANA_PASSWORD="$(kubectl get secret grafana-admin-secret -n "$NAMESPACE" -o jsonpath='{.data.GF_SECURITY_ADMIN_PASSWORD}' 2>/dev/null | base64 -d 2>/dev/null || true)"
GRAFANA_DASHBOARD="$(curl -fsS --max-time 10 -u "admin:${GRAFANA_PASSWORD}" http://127.0.0.1:13000/api/dashboards/uid/mcpserver-observability 2>/dev/null || true)"
echo "$GRAFANA_DASHBOARD" | grep -q 'MCP Server - Observability Intelligence' &&
  pass "Grafana MCP dashboard is provisioned" || fail "Grafana MCP dashboard is missing"

section "Ingress session affinity and MCP tool surface"
curl -sS --max-time 15 -D "$WORK_DIR/mcp-headers" -c "$WORK_DIR/mcp-cookies" \
  -H 'Host: mcpserver.local' \
  -H 'Content-Type: application/json' \
  -H 'Accept: application/json, text/event-stream' \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"phase7","version":"1.0"}}}' \
  "${INGRESS_BASE_URL}/" >"$WORK_DIR/mcp-init" 2>/dev/null || true

SESSION_ID="$(awk 'BEGIN{IGNORECASE=1} /^Mcp-Session-Id:/ {gsub("\r", "", $2); print $2}' "$WORK_DIR/mcp-headers" | tail -1)"
if [[ -n "$SESSION_ID" ]] && grep -q 'mcp-route' "$WORK_DIR/mcp-cookies"; then
  pass "Ingress issued both MCP session and affinity cookie"
else
  fail "MCP session or affinity cookie is missing"
fi

if [[ -n "$SESSION_ID" ]]; then
  curl -sS --max-time 10 -b "$WORK_DIR/mcp-cookies" \
    -H 'Host: mcpserver.local' \
    -H 'Content-Type: application/json' \
    -H 'Accept: application/json, text/event-stream' \
    -H "Mcp-Session-Id: $SESSION_ID" \
    -d '{"jsonrpc":"2.0","method":"notifications/initialized"}' \
    "${INGRESS_BASE_URL}/" >/dev/null 2>&1 || true
  curl -sS --max-time 15 -b "$WORK_DIR/mcp-cookies" \
    -H 'Host: mcpserver.local' \
    -H 'Content-Type: application/json' \
    -H 'Accept: application/json, text/event-stream' \
    -H "Mcp-Session-Id: $SESSION_ID" \
    -d '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' \
    "${INGRESS_BASE_URL}/" >"$WORK_DIR/mcp-tools" 2>/dev/null || true
fi

EXPECTED_TOOLS=(
  service_get_spec service_get_health service_get_score service_get_dependencies
  service_detect_anomalies service_get_incident_timeline service_find_root_cause
  system_get_health_summary
)
MISSING_TOOLS=()
for tool in "${EXPECTED_TOOLS[@]}"; do
  grep -q "\"${tool}\"" "$WORK_DIR/mcp-tools" 2>/dev/null || MISSING_TOOLS+=("$tool")
done
if [[ ${#MISSING_TOOLS[@]} -eq 0 ]]; then
  pass "all eight vNext MCP tools are exposed"
else
  fail "missing vNext MCP tools: ${MISSING_TOOLS[*]}"
fi
if ! grep -q '"query_metrics"' "$WORK_DIR/mcp-tools" 2>/dev/null; then
  pass "legacy raw query tool is absent by default"
else
  fail "legacy raw query tool is unexpectedly exposed"
fi

if $RUN_RESILIENCE; then
  section "Resilience: rollout, freshness and state"
  STATE_BEFORE="$(kubectl get configmap mcpserver-state -n "$NAMESPACE" -o jsonpath='{.data.indexing-overrides}' 2>/dev/null || true)"
  GENERATED_BEFORE="$(echo "$OVERVIEW_BEFORE" | grep -o '"generatedAt":"[^"]*"' | head -1 || true)"
  stop_port_forwards

  if kubectl rollout restart deployment/mcpserver -n "$NAMESPACE" >/dev/null 2>&1 &&
     kubectl rollout status deployment/mcpserver -n "$NAMESPACE" --timeout=240s >/dev/null 2>&1; then
    pass "zero-downtime rolling restart completed"
  else
    fail "rolling restart failed"
  fi

  if start_port_forwards; then
    pass "endpoints returned after rolling restart"
  else
    fail "endpoints did not return after rolling restart"
  fi
  expect_http "readiness after restart" "http://127.0.0.1:14000/ready" 200

  STATE_AFTER="$(kubectl get configmap mcpserver-state -n "$NAMESPACE" -o jsonpath='{.data.indexing-overrides}' 2>/dev/null || true)"
  if [[ "$STATE_BEFORE" == "$STATE_AFTER" ]]; then
    pass "administrative indexing state survived rollout"
  else
    fail "administrative indexing state changed during rollout"
  fi

  OVERVIEW_AFTER="$(curl -fsS --max-time 30 http://127.0.0.1:14000/api/dashboard/overview 2>/dev/null || true)"
  GENERATED_AFTER="$(echo "$OVERVIEW_AFTER" | grep -o '"generatedAt":"[^"]*"' | head -1 || true)"
  if [[ -n "$GENERATED_AFTER" && "$GENERATED_AFTER" != "$GENERATED_BEFORE" ]] &&
     echo "$OVERVIEW_AFTER" | grep -q '"sources"'; then
    pass "contract and freshness were preserved across rollout"
  else
    fail "contract/freshness after rollout"
  fi

  section "Resilience: active NetworkPolicy denial"
  kubectl create namespace "$TEST_NAMESPACE" --dry-run=client -o yaml | kubectl apply -f - >/dev/null 2>&1
  kubectl run phase7-denied -n "$TEST_NAMESPACE" --restart=Never \
    --image=curlimages/curl:8.10.1 --command -- sh -c \
    'if curl -fsS --connect-timeout 3 --max-time 5 http://mcpserver.mcp-apis.svc.cluster.local:4000/live; then echo UNEXPECTED_REACHABLE; exit 42; else echo EXPECTED_DENIED; exit 0; fi' \
    >/dev/null 2>&1 || true
  NETWORK_TEST_PHASE=""
  for _ in {1..45}; do
    NETWORK_TEST_PHASE="$(kubectl get pod phase7-denied -n "$TEST_NAMESPACE" \
      -o jsonpath='{.status.phase}' 2>/dev/null || true)"
    [[ "$NETWORK_TEST_PHASE" == "Succeeded" || "$NETWORK_TEST_PHASE" == "Failed" ]] && break
    sleep 1
  done
  if [[ "$NETWORK_TEST_PHASE" == "Succeeded" ]] &&
     kubectl logs phase7-denied -n "$TEST_NAMESPACE" 2>/dev/null | grep -q EXPECTED_DENIED; then
    pass "untrusted namespace was denied by NetworkPolicy"
  else
    NETWORK_TEST_LOG="$(kubectl logs phase7-denied -n "$TEST_NAMESPACE" 2>/dev/null || true)"
    fail "NetworkPolicy denial test ended phase=${NETWORK_TEST_PHASE:-unknown} log=${NETWORK_TEST_LOG:-none}"
  fi
fi

section "Result"
printf 'PHASE7_SUMMARY:pass=%d:fail=%d:resilience=%s\n' "$PASS" "$FAIL" "$RUN_RESILIENCE"
[[ "$FAIL" -eq 0 ]]
