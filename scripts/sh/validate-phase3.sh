#!/usr/bin/env bash
# validate-phase3.sh — Validate Phase 3 (Observability stack)
set -euo pipefail

export PATH="$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin"

NAMESPACE="mcp-apis"
PASS=0
FAIL=0

kubectl config use-context k3d-mcp-apis 2>/dev/null

# Start port-forwards
kubectl port-forward -n "${NAMESPACE}" svc/precoapi   5001:80 &
kubectl port-forward -n "${NAMESPACE}" svc/produtoapi 5002:80 &
kubectl port-forward -n "${NAMESPACE}" svc/prometheus 9090:9090 &
kubectl port-forward -n "${NAMESPACE}" svc/grafana    3000:3000 &
kubectl port-forward -n "${NAMESPACE}" svc/jaeger     16686:16686 &

trap 'kill $(jobs -p) 2>/dev/null; echo "Port-forwards stopped."' EXIT
sleep 6

echo "=== Phase 3 Validation ==="
echo ""

# Test 1: PrecoAPI /metrics
echo -n "1. PrecoAPI /metrics .......... "
BODY=$(curl -sf http://localhost:5001/metrics 2>/dev/null || echo "")
if echo "$BODY" | grep -q "http_server"; then
  echo "PASS"; PASS=$((PASS + 1))
else
  echo "FAIL"; FAIL=$((FAIL + 1))
fi

# Test 2: ProdutoAPI /metrics
echo -n "2. ProdutoAPI /metrics ........ "
BODY=$(curl -sf http://localhost:5002/metrics 2>/dev/null || echo "")
if echo "$BODY" | grep -q "http_server"; then
  echo "PASS"; PASS=$((PASS + 1))
else
  echo "FAIL"; FAIL=$((FAIL + 1))
fi

# Test 3: Prometheus UI
echo -n "3. Prometheus /api/v1/status .. "
CODE=$(curl -sf -o /dev/null -w '%{http_code}' http://localhost:9090/api/v1/status/config 2>/dev/null || echo "000")
if [[ "$CODE" == "200" ]]; then
  echo "PASS"; PASS=$((PASS + 1))
else
  echo "FAIL (HTTP $CODE)"; FAIL=$((FAIL + 1))
fi

# Test 4: Prometheus targets
echo -n "4. Prometheus targets UP ...... "
TARGETS=$(curl -sf http://localhost:9090/api/v1/targets 2>/dev/null || echo "")
UP_COUNT=$(echo "$TARGETS" | python3 -c "import sys,json; d=json.load(sys.stdin); print(sum(1 for t in d['data']['activeTargets'] if t['health']=='up'))" 2>/dev/null || echo "0")
if [[ "$UP_COUNT" -ge 2 ]]; then
  echo "PASS (${UP_COUNT} targets UP)"; PASS=$((PASS + 1))
else
  echo "FAIL (${UP_COUNT} targets UP)"; FAIL=$((FAIL + 1))
fi

# Test 5: Grafana login
echo -n "5. Grafana /api/health ........ "
CODE=$(curl -sf -o /dev/null -w '%{http_code}' http://localhost:3000/api/health 2>/dev/null || echo "000")
if [[ "$CODE" == "200" ]]; then
  echo "PASS"; PASS=$((PASS + 1))
else
  echo "FAIL (HTTP $CODE)"; FAIL=$((FAIL + 1))
fi

# Test 6: Grafana datasources
echo -n "6. Grafana datasources ....... "
DS=$(curl -sf -u admin:admin http://localhost:3000/api/datasources 2>/dev/null || echo "[]")
DS_COUNT=$(echo "$DS" | python3 -c "import sys,json; print(len(json.load(sys.stdin)))" 2>/dev/null || echo "0")
if [[ "$DS_COUNT" -ge 3 ]]; then
  echo "PASS (${DS_COUNT} datasources)"; PASS=$((PASS + 1))
else
  echo "FAIL (${DS_COUNT} datasources)"; FAIL=$((FAIL + 1))
fi

# Test 7: Jaeger still works
echo -n "7. Jaeger /api/services ....... "
CODE=$(curl -sf -o /dev/null -w '%{http_code}' http://localhost:16686/api/services 2>/dev/null || echo "000")
if [[ "$CODE" == "200" ]]; then
  echo "PASS"; PASS=$((PASS + 1))
else
  echo "FAIL (HTTP $CODE)"; FAIL=$((FAIL + 1))
fi

echo ""
echo "=== Results: ${PASS} PASSED / ${FAIL} FAILED ==="
[[ $FAIL -eq 0 ]] && exit 0 || exit 1
