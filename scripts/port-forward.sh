#!/usr/bin/env bash
# port-forward.sh — Forward all mcp-apis services to localhost (no hosts file needed)
# Usage: bash scripts/port-forward.sh
#   PrecoAPI   → http://localhost:5001
#   ProdutoAPI → http://localhost:5002
#   Jaeger     → http://localhost:16686
set -euo pipefail

NAMESPACE="mcp-apis"
CLUSTER_CONTEXT="k3d-mcp-apis"

export PATH="$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin"

kubectl config use-context "${CLUSTER_CONTEXT}" 2>/dev/null

echo "📡 Starting port-forwards (Ctrl+C to stop all)..."
echo ""
echo "   PrecoAPI   → http://localhost:5001/api/prices"
echo "   PrecoAPI   → http://localhost:5001/scalar/v1"
echo "   ProdutoAPI → http://localhost:5002/api/products"
echo "   ProdutoAPI → http://localhost:5002/scalar/v1"
echo "   Jaeger     → http://localhost:16686"
echo ""

# Forward all in background, track PIDs
kubectl port-forward -n "${NAMESPACE}" svc/precoapi   5001:80 &
PF1=$!
kubectl port-forward -n "${NAMESPACE}" svc/produtoapi 5002:80 &
PF2=$!
kubectl port-forward -n "${NAMESPACE}" svc/jaeger     16686:16686 &
PF3=$!

# Kill all forwards on Ctrl+C
trap "kill $PF1 $PF2 $PF3 2>/dev/null; echo 'Port-forwards stopped.'" INT TERM

wait $PF1 $PF2 $PF3
