#!/usr/bin/env bash
# teardown-k8s.sh — Remove o namespace mcp-apis (e todos os recursos dentro dele).
# O cluster k3d NAO e deletado; use 'k3d cluster delete mcp-apis' manualmente se necessario.
set -euo pipefail

NAMESPACE="mcp-apis"

echo "Deletando namespace '${NAMESPACE}'..."
kubectl delete namespace "${NAMESPACE}" --ignore-not-found=true

echo "[OK]  Namespace removido. Cluster k3d permanece intacto."
