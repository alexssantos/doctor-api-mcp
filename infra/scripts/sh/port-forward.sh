#!/usr/bin/env bash
# port-forward.sh — Redireciona todos os servicos mcp-apis para localhost.
# Uso: bash infra/scripts/sh/port-forward.sh
#   PrecoAPI   -> http://localhost:5001
#   ProdutoAPI -> http://localhost:5002
#   McpServer  -> http://localhost:4000
#   Jaeger     -> http://localhost:16686
#   Prometheus -> http://localhost:9090
#   Grafana    -> http://localhost:3000  (admin/admin)
set -euo pipefail

NAMESPACE="mcp-apis"
CLUSTER_CONTEXT="k3d-mcp-apis"

export PATH="$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin"

kubectl config use-context "${CLUSTER_CONTEXT}" 2>/dev/null

echo "[NET] Iniciando port-forwards (Ctrl+C para parar)..."
echo ""
echo "   PrecoAPI   -> http://localhost:5001/scalar/v1"
echo "   ProdutoAPI -> http://localhost:5002/scalar/v1"
echo "   McpServer  -> http://localhost:4000/health"
echo "   Jaeger     -> http://localhost:16686"
echo "   Prometheus -> http://localhost:9090"
echo "   Grafana    -> http://localhost:3000  (admin/admin)"
echo ""

# kubectl port-forward com svc porta 80 trava no kubectl >= v1.29 em alguns casos.
# Para precoapi/produtoapi (service port 80 -> pod port 8080): usar pod direto.
PRECOAPI_POD=$(kubectl get pod -n "${NAMESPACE}" -l app=precoapi \
  --no-headers 2>/dev/null | awk 'NR==1 && $3=="Running" {print $1}')
PRODUTOAPI_POD=$(kubectl get pod -n "${NAMESPACE}" -l app=produtoapi \
  --no-headers 2>/dev/null | awk 'NR==1 && $3=="Running" {print $1}')

PIDS=()

if [[ -n "$PRECOAPI_POD" ]]; then
  kubectl port-forward -n "${NAMESPACE}" "pod/${PRECOAPI_POD}" 5001:8080 \
    >/tmp/pf_precoapi.log 2>&1 & PIDS+=($!)
else
  echo "[WARN] precoapi: nenhum pod Running — port-forward ignorado"
fi

if [[ -n "$PRODUTOAPI_POD" ]]; then
  kubectl port-forward -n "${NAMESPACE}" "pod/${PRODUTOAPI_POD}" 5002:8080 \
    >/tmp/pf_produtoapi.log 2>&1 & PIDS+=($!)
else
  echo "[WARN] produtoapi: nenhum pod Running — port-forward ignorado"
fi

kubectl port-forward -n "${NAMESPACE}" svc/mcpserver  4000:4000  >/tmp/pf_mcpserver.log  2>&1 & PIDS+=($!)
kubectl port-forward -n "${NAMESPACE}" svc/jaeger     16686:16686 >/tmp/pf_jaeger.log     2>&1 & PIDS+=($!)
kubectl port-forward -n "${NAMESPACE}" svc/prometheus 9090:9090   >/tmp/pf_prometheus.log 2>&1 & PIDS+=($!)
kubectl port-forward -n "${NAMESPACE}" svc/grafana    3000:3000   >/tmp/pf_grafana.log    2>&1 & PIDS+=($!)

trap 'echo ""; echo "Encerrando port-forwards..."; kill "${PIDS[@]}" 2>/dev/null; echo "[OK]  Encerrado."' INT TERM

wait "${PIDS[@]}"
