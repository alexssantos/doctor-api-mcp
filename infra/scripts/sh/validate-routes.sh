#!/usr/bin/env bash
# validate-routes.sh — end-to-end smoke test (manages its own port-forwards)
# Usage: bash scripts/validate-routes.sh
set -euo pipefail

export PATH="$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin"

NAMESPACE="mcp-apis"
PRECO_URL="http://localhost:5001"
PRODUTO_URL="http://localhost:5002"
PASS=0; FAIL=0

# ─── Start port-forwards ──────────────────────────────────────────────────────
echo "📡 Starting port-forwards..."
kubectl config use-context k3d-mcp-apis >/dev/null 2>&1
kubectl port-forward -n "${NAMESPACE}" svc/precoapi   5001:80 >/dev/null 2>&1 &
PF1=$!
kubectl port-forward -n "${NAMESPACE}" svc/produtoapi 5002:80 >/dev/null 2>&1 &
PF2=$!
trap "kill \$PF1 \$PF2 2>/dev/null; echo ''; echo 'Port-forwards stopped.'" EXIT
sleep 6

check() {
  local label="$1" expected="$2" actual="$3"
  if [[ "$actual" == "$expected" ]]; then
    echo "  [OK]  $label → HTTP $actual"
    PASS=$((PASS + 1))
  else
    echo "  [FAIL] $label → expected $expected, got $actual"
    FAIL=$((FAIL + 1))
  fi
}

echo "=== 1. OpenAPI spec e Scalar UI ==="
check "PrecoAPI   GET /openapi/v1.json"   200 "$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 "$PRECO_URL/openapi/v1.json")"
check "PrecoAPI   GET /scalar/v1"         200 "$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 "$PRECO_URL/scalar/v1")"
check "ProdutoAPI GET /openapi/v1.json"   200 "$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 "$PRODUTO_URL/openapi/v1.json")"
check "ProdutoAPI GET /scalar/v1"         200 "$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 "$PRODUTO_URL/scalar/v1")"

echo ""
echo "=== 2. POST /api/products ==="
PRODUTO=$(curl -s --max-time 10 -X POST "$PRODUTO_URL/api/products" \
  -H "Content-Type: application/json" \
  -d '{"name":"Notebook Gamer","description":"16GB RAM","sku":"NB-GAMER-01"}')
echo "  Body: $PRODUTO"
PROD_ID=$(echo "$PRODUTO" | tr ',' '\n' | grep '"id"' | head -1 | grep -o '[0-9a-f-]\{36\}')
echo "  ID extraído: $PROD_ID"
if [[ -n "$PROD_ID" ]]; then echo "  [OK]  Produto criado"; PASS=$((PASS + 1)); else echo "  [FAIL] Falha ao criar produto"; FAIL=$((FAIL + 1)); fi

echo ""
echo "=== 3. POST /api/prices (para o produto criado) ==="
PRECO=$(curl -s --max-time 10 -X POST "$PRECO_URL/api/prices" \
  -H "Content-Type: application/json" \
  -d "{\"productId\":\"$PROD_ID\",\"value\":4999.99,\"currency\":\"BRL\"}")
echo "  Body: $PRECO"
if echo "$PRECO" | grep -q '"value"'; then echo "  [OK]  Preço criado"; PASS=$((PASS + 1)); else echo "  [FAIL] Falha ao criar preço"; FAIL=$((FAIL + 1)); fi

echo ""
echo "=== 4. GET /api/prices/:productId ==="
PRECO_GET=$(curl -s --max-time 10 "$PRECO_URL/api/prices/$PROD_ID")
echo "  Body: $PRECO_GET"
check "PrecoAPI GET /api/prices/$PROD_ID" 200 "$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 "$PRECO_URL/api/prices/$PROD_ID")"

echo ""
echo "=== 5. GET /api/products (lista com preço) ==="
LISTA=$(curl -s --max-time 10 "$PRODUTO_URL/api/products")
echo "  Body: $LISTA"
if echo "$LISTA" | grep -q '"value"'; then echo "  [OK]  Price populado no produto"; PASS=$((PASS + 1)); else echo "  [FAIL] Price null — integração PrecoAPI falhou"; FAIL=$((FAIL + 1)); fi

echo ""
echo "=== 6. GET /api/products/:id ==="
PROD_GET=$(curl -s --max-time 10 "$PRODUTO_URL/api/products/$PROD_ID")
echo "  Body: $PROD_GET"
check "ProdutoAPI GET /api/products/$PROD_ID" 200 "$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 "$PRODUTO_URL/api/products/$PROD_ID")"

echo ""
echo "=== 7. DELETE ==="
check "PrecoAPI   DELETE /api/prices/$PROD_ID"   204 "$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 -X DELETE "$PRECO_URL/api/prices/$PROD_ID")"
check "ProdutoAPI DELETE /api/products/$PROD_ID" 204 "$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 -X DELETE "$PRODUTO_URL/api/products/$PROD_ID")"

echo ""
echo "══════════════════════════════"
echo "  PASSOU: $PASS   FALHOU: $FAIL"
echo "══════════════════════════════"
[[ $FAIL -eq 0 ]]
