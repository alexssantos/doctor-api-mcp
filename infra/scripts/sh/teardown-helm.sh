#!/usr/bin/env bash
# teardown-helm.sh — Uninstall all Helm releases and delete the k3d cluster
set -euo pipefail

CLUSTER_NAME="mcp-apis"
NAMESPACE="mcp-apis"

echo "🗑️  Uninstalling Helm releases from namespace '${NAMESPACE}'..."
for release in produtoapi precoapi postgres-produto postgres-preco; do
  if helm status "$release" -n "${NAMESPACE}" &>/dev/null; then
    helm uninstall "$release" -n "${NAMESPACE}"
    echo "   [OK]  $release uninstalled"
  else
    echo "   ℹ️  $release not found — skipping"
  fi
done

echo "🗑️  Deleting namespace '${NAMESPACE}'..."
kubectl delete namespace "${NAMESPACE}" --ignore-not-found=true

echo "💥 Deleting k3d cluster '${CLUSTER_NAME}'..."
k3d cluster delete "${CLUSTER_NAME}"

echo "[OK]  Teardown complete."
