#!/usr/bin/env bash
# teardown-k8s.sh — Remove mcp-apis namespace and delete the k3d cluster
set -euo pipefail

CLUSTER_NAME="mcp-apis"
NAMESPACE="mcp-apis"

echo "🗑️  Deleting namespace '${NAMESPACE}'..."
kubectl delete namespace "${NAMESPACE}" --ignore-not-found=true

echo "💥 Deleting k3d cluster '${CLUSTER_NAME}'..."
k3d cluster delete "${CLUSTER_NAME}"

echo "✅ Teardown complete."
