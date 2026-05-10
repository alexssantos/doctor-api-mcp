#!/usr/bin/env bash
# validate-phase4.sh — Validate Phase 4 (MCP Server)
set -euo pipefail

export PATH="$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin"

NAMESPACE="mcp-apis"
PASS=0
FAIL=0

kubectl config use-context k3d-mcp-apis 2>/dev/null

# Start port-forward
kubectl port-forward -n "${NAMESPACE}" svc/mcpserver 4000:4000 &
PF1=$!
trap 'kill $PF1 2>/dev/null; echo "Port-forwards stopped."' EXIT
sleep 4

echo "=== Phase 4 Validation ==="
echo ""

# Test 1: Health endpoint
echo -n "1. /health endpoint .......... "
BODY=$(curl -sf http://localhost:4000/health 2>/dev/null || echo "")
if echo "$BODY" | grep -q "healthy"; then
  echo "PASS"; PASS=$((PASS + 1))
else
  echo "FAIL"; FAIL=$((FAIL + 1))
fi

# Test 2: MCP initialize (Streamable HTTP at /)
echo -n "2. MCP initialize ............ "
RESP=$(curl -sf -X POST http://localhost:4000/ \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}' 2>/dev/null || echo "")
if echo "$RESP" | grep -q "mcp-apis-server"; then
  echo "PASS"; PASS=$((PASS + 1))
else
  echo "FAIL"; FAIL=$((FAIL + 1))
fi

# Extract session ID for subsequent requests
SESSION_ID=$(curl -sf -X POST http://localhost:4000/ \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -d '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-03-26","capabilities":{},"clientInfo":{"name":"test","version":"1.0"}}}' \
  -D /dev/stderr 2>&1 1>/dev/null | grep "Mcp-Session-Id:" | tr -d '\r' | awk '{print $2}')

# Send initialized notification
curl -sf -X POST http://localhost:4000/ \
  -H "Content-Type: application/json" \
  -H "Accept: application/json" \
  -H "Mcp-Session-Id: ${SESSION_ID}" \
  -d '{"jsonrpc":"2.0","method":"notifications/initialized"}' >/dev/null 2>&1 || true

# Test 3: tools/list
echo -n "3. MCP tools/list ............ "
TOOLS_RESP=$(curl -sf -X POST http://localhost:4000/ \
  -H "Content-Type: application/json" \
  -H "Accept: application/json, text/event-stream" \
  -H "Mcp-Session-Id: ${SESSION_ID}" \
  -d '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' 2>/dev/null || echo "")
TOOL_COUNT=$(echo "$TOOLS_RESP" | grep -o '"name"' | wc -l)
if [[ "$TOOL_COUNT" -ge 7 ]]; then
  echo "PASS (${TOOL_COUNT} tools)"; PASS=$((PASS + 1))
else
  echo "FAIL (${TOOL_COUNT} tools found)"; FAIL=$((FAIL + 1))
  echo "  Response: ${TOOLS_RESP:0:200}"
fi

# Test 4: Verify expected tool names
echo -n "4. Expected tool names ....... "
EXPECTED_TOOLS=("list_services" "get_openapi" "trace_route" "explain_api" "get_health" "find_dependencies" "find_data_origin")
MISSING=""
for tool in "${EXPECTED_TOOLS[@]}"; do
  if ! echo "$TOOLS_RESP" | grep -q "\"$tool\""; then
    MISSING="${MISSING} ${tool}"
  fi
done
if [[ -z "$MISSING" ]]; then
  echo "PASS"; PASS=$((PASS + 1))
else
  echo "FAIL (missing:${MISSING})"; FAIL=$((FAIL + 1))
fi

# Test 5: Pod running + ready
echo -n "5. MCP Server pod ready ...... "
POD_READY=$(kubectl get pods -n "${NAMESPACE}" -l app=mcpserver -o jsonpath='{.items[0].status.containerStatuses[0].ready}' 2>/dev/null || echo "false")
if [[ "$POD_READY" == "true" ]]; then
  echo "PASS"; PASS=$((PASS + 1))
else
  echo "FAIL"; FAIL=$((FAIL + 1))
fi

# Test 6: ServiceAccount + RBAC
echo -n "6. RBAC (mcp-reader SA) ...... "
SA=$(kubectl get serviceaccount mcp-reader -n "${NAMESPACE}" -o name 2>/dev/null || echo "")
ROLE=$(kubectl get role mcp-reader-role -n "${NAMESPACE}" -o name 2>/dev/null || echo "")
if [[ -n "$SA" && -n "$ROLE" ]]; then
  echo "PASS"; PASS=$((PASS + 1))
else
  echo "FAIL"; FAIL=$((FAIL + 1))
fi

echo ""
echo "=== Results: ${PASS} PASSED / ${FAIL} FAILED ==="
[[ $FAIL -eq 0 ]] && exit 0 || exit 1
