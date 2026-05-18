#!/usr/bin/env bash
# docker/health-check.sh — Verifica se todos os serviços estão rodando via Docker
# Usage: bash scripts/docker/health-check.sh
set -euo pipefail

export PATH="$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin"

PASS=0
FAIL=0

GREEN='\033[0;32m'
RED='\033[0;31m'
YELLOW='\033[1;33m'
NC='\033[0m'

pass() { echo -e "  ${GREEN}✅ $1${NC}"; PASS=$((PASS + 1)); }
fail() { echo -e "  ${RED}❌ $1${NC}"; FAIL=$((FAIL + 1)); }
warn() { echo -e "  ${YELLOW}⚠️  $1${NC}"; }
section() { echo ""; echo "=== $1 ==="; }

# ─── Portas locais esperadas ───────────────────────────────────────────────────
PRECO_URL="http://localhost:5001"
PRODUTO_URL="http://localhost:5002"
MCP_URL="http://localhost:4000"
PROMETHEUS_URL="http://localhost:9090"
GRAFANA_URL="http://localhost:3000"
JAEGER_URL="http://localhost:16686"

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

container_check() {
  local label="$1" image_pattern="$2"
  local count
  count=$(docker ps --format '{{.Image}}' 2>/dev/null | grep -c "$image_pattern" || echo "0")
  if [[ "$count" -gt 0 ]]; then
    pass "$label → $count container(s) rodando"
  else
    fail "$label → nenhum container rodando com imagem '$image_pattern'"
  fi
}

image_check() {
  local label="$1" image="$2"
  if docker image inspect "$image" &>/dev/null; then
    local created
    created=$(docker image inspect "$image" --format '{{.Created}}' 2>/dev/null | cut -c1-10)
    pass "$label → imagem presente (criada: $created)"
  else
    fail "$label → imagem não encontrada. Execute o build primeiro."
  fi
}

echo "╔══════════════════════════════════════════════╗"
echo "║     mcp-apis — Docker Health Check           ║"
echo "╚══════════════════════════════════════════════╝"

# ─── 1. Docker daemon ─────────────────────────────────────────────────────────
section "1. Docker Daemon"
if docker info &>/dev/null; then
  DOCKER_VERSION=$(docker version --format '{{.Server.Version}}' 2>/dev/null || echo "desconhecido")
  pass "Docker daemon respondendo (versão $DOCKER_VERSION)"
else
  fail "Docker daemon não está rodando"
  echo ""
  echo "Docker está parado. Não é possível continuar."
  exit 1
fi

# ─── 2. Imagens construídas ───────────────────────────────────────────────────
section "2. Imagens Docker"
image_check "PrecoAPI"   "precoapi:latest"
image_check "ProdutoAPI" "produtoapi:latest"
image_check "McpServer"  "mcpserver:latest"

# ─── 3. Containers rodando ────────────────────────────────────────────────────
section "3. Containers em execução"
container_check "PrecoAPI"         "precoapi"
container_check "ProdutoAPI"       "produtoapi"
container_check "McpServer"        "mcpserver"
container_check "PostgreSQL Preco" "postgres"
container_check "Jaeger"           "jaegertracing"
container_check "Prometheus"       "prom/prometheus"
container_check "Grafana"          "grafana/grafana"

# ─── 4. Endpoints HTTP ────────────────────────────────────────────────────────
section "4. Endpoints HTTP"
http_check "PrecoAPI   /metrics"             "$PRECO_URL/metrics"
http_check "PrecoAPI   /scalar/v1"           "$PRECO_URL/scalar/v1"
http_check "ProdutoAPI /metrics"             "$PRODUTO_URL/metrics"
http_check "ProdutoAPI /scalar/v1"           "$PRODUTO_URL/scalar/v1"
http_check "McpServer  /health"              "$MCP_URL/health"
http_check "Prometheus /api/v1/status"       "$PROMETHEUS_URL/api/v1/status/config"
http_check "Grafana    /api/health"          "$GRAFANA_URL/api/health"
http_check "Jaeger     UI"                   "$JAEGER_URL"

# ─── 5. Integração PrecoAPI → ProdutoAPI ─────────────────────────────────────
section "5. Integração entre serviços"
PRODUTOS=$(curl -s --max-time 5 "$PRODUTO_URL/api/products" 2>/dev/null || echo "")
if [[ -n "$PRODUTOS" && "$PRODUTOS" != "000" ]]; then
  pass "ProdutoAPI GET /api/products respondendo"
  if echo "$PRODUTOS" | grep -q '"value"'; then
    pass "Integração ProdutoAPI → PrecoAPI: campo 'value' presente"
  else
    warn "Integração ProdutoAPI → PrecoAPI: campo 'value' ausente (sem dados ou PrecoAPI offline)"
  fi
else
  fail "ProdutoAPI GET /api/products sem resposta"
fi

# ─── 6. MCP Server — lista de ferramentas ─────────────────────────────────────
section "6. MCP Server — tools"
MCP_RESP=$(curl -s -X POST "$MCP_URL/" \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  --max-time 5 \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"healthcheck","version":"1.0"}}}' \
  2>/dev/null || echo "")
if echo "$MCP_RESP" | grep -q "mcp-apis-server"; then
  pass "MCP Server initialize respondendo"
else
  fail "MCP Server initialize sem resposta esperada"
fi

# ─── Resumo ───────────────────────────────────────────────────────────────────
echo ""
echo "══════════════════════════════════════════════"
TOTAL=$((PASS + FAIL))
echo -e "  Resultado: ${GREEN}${PASS} passou${NC} / ${RED}${FAIL} falhou${NC} (total: $TOTAL)"
echo "══════════════════════════════════════════════"
echo ""

if [[ "$FAIL" -gt 0 ]]; then
  exit 1
fi
