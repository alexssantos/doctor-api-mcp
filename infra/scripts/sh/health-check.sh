#!/usr/bin/env bash
# k8s/health-check.sh — Verifica se todos os serviços estão up no Kubernetes
# Usage: bash scripts/k8s/health-check.sh
set -euo pipefail

export PATH="$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin"

NAMESPACE="mcp-apis"
CLUSTER_CONTEXT="k3d-mcp-apis"
PASS=0
FAIL=0
PF_PIDS=()

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'

pass() { echo -e "  ${GREEN}[OK]  $1${NC}"; PASS=$((PASS + 1)); }
fail() { echo -e "  ${RED}[FAIL] $1${NC}"; FAIL=$((FAIL + 1)); }
warn() { echo -e "  ${YELLOW}[WARN] $1${NC}"; }
section() { echo ""; echo "=== $1 ==="; }

cleanup() {
  if [[ ${#PF_PIDS[@]} -gt 0 ]]; then
    kill "${PF_PIDS[@]}" 2>/dev/null || true
    echo ""
    echo "Port-forwards encerrados."
  fi
}
trap cleanup EXIT

http_check() {
  local label="$1" url="$2" expected="${3:-200}"
  local code
  code=$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 "$url" 2>/dev/null || echo "000")
  if [[ "$code" == "$expected" ]]; then
    pass "$label → HTTP $code"
  else
    fail "$label → esperado HTTP $expected, obtido HTTP $code ($url)"
  fi
}

http_body_check() {
  local label="$1" url="$2" pattern="$3"
  local body
  body=$(curl -s --max-time 5 "$url" 2>/dev/null || echo "")
  if echo "$body" | grep -q "$pattern"; then
    pass "$label"
  else
    fail "$label (padrão '$pattern' não encontrado em $url)"
  fi
}

echo "╔══════════════════════════════════════════════╗"
echo "║   mcp-apis — Kubernetes Health Check         ║"
echo "╚══════════════════════════════════════════════╝"

# ─── 1. Ferramentas ───────────────────────────────────────────────────────────
section "1. Ferramentas"
for cmd in kubectl k3d curl; do
  if command -v "$cmd" &>/dev/null; then
    pass "$cmd disponível"
  else
    fail "$cmd não encontrado no PATH"
  fi
done

# ─── 2. Cluster k3d ───────────────────────────────────────────────────────────
section "2. Cluster k3d"
if k3d cluster list --no-headers 2>/dev/null | awk '{print $1}' | grep -q "^mcp-apis$"; then
  STATUS=$(k3d cluster list --no-headers 2>/dev/null | awk '/^mcp-apis/{print $2}')
  pass "Cluster 'mcp-apis' existe (servidores: $STATUS)"
else
  fail "Cluster 'mcp-apis' não encontrado. Execute deploy-k8s.sh ou deploy-helm.sh"
  echo ""
  echo "Cluster não encontrado. Não é possível continuar."
  exit 1
fi

kubectl config use-context "${CLUSTER_CONTEXT}" >/dev/null 2>&1
pass "Contexto kubectl definido para '${CLUSTER_CONTEXT}'"

# ─── 3. Namespace ─────────────────────────────────────────────────────────────
section "3. Namespace"
if kubectl get namespace "${NAMESPACE}" &>/dev/null; then
  pass "Namespace '${NAMESPACE}' existe"
else
  fail "Namespace '${NAMESPACE}' não encontrado"
  exit 1
fi

# ─── 4. Pods ──────────────────────────────────────────────────────────────────
section "4. Pods"

# Tenta encontrar pod por 3 selectors:
#   1. app=X                           (raw k8s manifests: jaeger, prometheus, etc.)
#   2. app.kubernetes.io/name=X        (Helm charts: precoapi, produtoapi, mcpserver)
#   3. app.kubernetes.io/instance=X    (Bitnami: postgres-produto, postgres-preco)
get_pod_status() {
  local name="$1"
  local result
  result=$(kubectl get pods -n "${NAMESPACE}" -l "app=${name}" --no-headers 2>/dev/null | awk '$3=="Running"{print $3" "$2; exit}')
  if [[ -z "$result" ]]; then
    result=$(kubectl get pods -n "${NAMESPACE}" -l "app.kubernetes.io/name=${name}" --no-headers 2>/dev/null | awk '$3=="Running"{print $3" "$2; exit}')
  fi
  if [[ -z "$result" ]]; then
    result=$(kubectl get pods -n "${NAMESPACE}" -l "app.kubernetes.io/instance=${name}" --no-headers 2>/dev/null | awk '$3=="Running"{print $3" "$2; exit}')
  fi
  echo "$result"
}

APPS=(precoapi produtoapi mcpserver postgres-produto postgres-preco jaeger prometheus grafana loki promtail)
for app in "${APPS[@]}"; do
  info=$(get_pod_status "$app")
  if [[ -z "$info" ]]; then
    fail "Pod $app → nenhum pod encontrado"
  else
    status=$(echo "$info" | awk '{print $1}')
    ready=$(echo "$info"  | awk '{print $2}')
    if [[ "$status" == "Running" ]]; then
      pass "Pod $app → Running ($ready)"
    else
      fail "Pod $app → $status ($ready)"
    fi
  fi
done

# ─── 5. Deployments / StatefulSets ────────────────────────────────────────────
section "5. Deployments e StatefulSets"

while IFS= read -r line; do
  name=$(echo "$line" | awk '{print $1}')
  ready=$(echo "$line" | awk '{print $2}')
  desired=$(echo "$ready" | cut -d'/' -f2)
  current=$(echo "$ready" | cut -d'/' -f1)
  if [[ "$current" == "$desired" && "$desired" != "0" ]]; then
    pass "Deployment $name → $ready pronto"
  else
    fail "Deployment $name → $ready (nem todos os pods prontos)"
  fi
done < <(kubectl get deployments -n "${NAMESPACE}" --no-headers 2>/dev/null)

while IFS= read -r line; do
  name=$(echo "$line" | awk '{print $1}')
  ready=$(echo "$line" | awk '{print $2}')
  desired=$(echo "$ready" | cut -d'/' -f2)
  current=$(echo "$ready" | cut -d'/' -f1)
  if [[ "$current" == "$desired" && "$desired" != "0" ]]; then
    pass "StatefulSet $name → $ready pronto"
  else
    fail "StatefulSet $name → $ready (nem todos os pods prontos)"
  fi
done < <(kubectl get statefulsets -n "${NAMESPACE}" --no-headers 2>/dev/null)

# ─── 6. Port-forwards ─────────────────────────────────────────────────────────
section "6. Iniciando port-forwards"
echo "  Aguardando serviços ficarem acessíveis..."

start_pf() {
  local svc="$1" local_port="$2" remote_port="$3"
  kubectl port-forward -n "${NAMESPACE}" "svc/${svc}" "${local_port}:${remote_port}" \
    >"/tmp/pf_${svc}.log" 2>&1 &
  PF_PIDS+=($!)
}

# Para serviços com service port 80 -> targetPort 8080,
# kubectl port-forward svc/ trava — usa pod direto
start_pf_pod() {
  local svc="$1" local_port="$2" pod_port="$3"
  local pod
  pod=$(kubectl get pod -n "${NAMESPACE}" -l "app=${svc}" --no-headers 2>/dev/null | awk 'NR==1{print $1}')
  if [[ -z "$pod" ]]; then
    pod=$(kubectl get pod -n "${NAMESPACE}" -l "app.kubernetes.io/name=${svc}" --no-headers 2>/dev/null | awk 'NR==1{print $1}')
  fi
  if [[ -z "$pod" ]]; then
    warn "pf/${svc}: nenhum pod encontrado"
    return
  fi
  kubectl port-forward -n "${NAMESPACE}" "pod/${pod}" "${local_port}:${pod_port}" \
    >"/tmp/pf_${svc}.log" 2>&1 &
  PF_PIDS+=($!)
}

start_pf_pod precoapi   5001 8080
start_pf_pod produtoapi 5002 8080
start_pf mcpserver  4000 4000
start_pf prometheus 9090 9090
start_pf grafana    3000 3000
start_pf jaeger     16686 16686

sleep 6
pass "Port-forwards iniciados (${#PF_PIDS[@]} processos)"

# ─── 7. Endpoints HTTP ────────────────────────────────────────────────────────
section "7. Endpoints HTTP"
http_check "PrecoAPI   /metrics"          "http://localhost:5001/metrics"
http_check "PrecoAPI   /openapi/v1.json"  "http://localhost:5001/openapi/v1.json"
http_check "PrecoAPI   /scalar/v1"        "http://localhost:5001/scalar/v1"
http_check "ProdutoAPI /metrics"          "http://localhost:5002/metrics"
http_check "ProdutoAPI /openapi/v1.json"  "http://localhost:5002/openapi/v1.json"
http_check "ProdutoAPI /scalar/v1"        "http://localhost:5002/scalar/v1"
http_check "McpServer  /health"           "http://localhost:4000/health"
http_check "McpServer  /live"             "http://localhost:4000/live"
http_check "McpServer  /ready"            "http://localhost:4000/ready"
http_check "Prometheus /api/v1/status"    "http://localhost:9090/api/v1/status/config"
http_check "Grafana    /api/health"       "http://localhost:3000/api/health"
http_check "Jaeger     UI"                "http://localhost:16686"

# ─── 8. Saúde do conteúdo ─────────────────────────────────────────────────────
section "8. Conteúdo dos endpoints"
http_body_check "PrecoAPI   /metrics contém 'http_server'"   "http://localhost:5001/metrics"          "http_server"
http_body_check "ProdutoAPI /metrics contém 'http_server'"   "http://localhost:5002/metrics"          "http_server"
http_body_check "McpServer  /health resposta 'healthy'"      "http://localhost:4000/health"            "healthy"
http_body_check "Grafana    datasources configurados"        "http://localhost:3000/api/health"        "ok"

# Prometheus targets
section "9. Prometheus targets"
TARGETS=$(curl -s --max-time 5 "http://localhost:9090/api/v1/targets" 2>/dev/null || echo "")
if [[ -n "$TARGETS" ]]; then
  ACTIVE_COUNT=$(echo "$TARGETS" | python3 -c \
    "import sys,json; d=json.load(sys.stdin); print(len(d['data']['activeTargets']))" \
    2>/dev/null || echo "0")
  UP_COUNT=$(echo "$TARGETS" | python3 -c \
    "import sys,json; d=json.load(sys.stdin); print(sum(1 for t in d['data']['activeTargets'] if t['health']=='up'))" \
    2>/dev/null || echo "0")
  if [[ "$ACTIVE_COUNT" -ge 2 ]]; then
    pass "Prometheus: $ACTIVE_COUNT target(s) configurado(s), $UP_COUNT UP"
  else
    fail "Prometheus: apenas $ACTIVE_COUNT target(s) configurado(s) (esperado ≥ 2)"
  fi
else
  fail "Prometheus: sem resposta em /api/v1/targets"
fi

# ─── 10. MCP Server — initialize ──────────────────────────────────────────────
section "10. MCP Server — protocolo"
MCP_RESP=$(curl -s -X POST "http://localhost:4000/" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  --max-time 5 \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"healthcheck","version":"1.0"}}}' \
  2>/dev/null || echo "")
if echo "$MCP_RESP" | grep -q "mcp-apis-server"; then
  pass "MCP initialize respondeu com serverInfo correto"
else
  fail "MCP initialize sem resposta esperada"
fi

# ─── Resumo ───────────────────────────────────────────────────────────────────
echo ""
echo "══════════════════════════════════════════════"
TOTAL=$((PASS + FAIL))
echo -e "  Resultado: ${GREEN}${PASS} passou${NC} / ${RED}${FAIL} falhou${NC} (total: $TOTAL)"
echo "══════════════════════════════════════════════"
echo ""

echo "HEALTH_SUMMARY:pass=${PASS}:fail=${FAIL}"

if [[ "$FAIL" -gt 0 ]]; then
  exit 1
fi
