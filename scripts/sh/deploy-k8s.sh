#!/usr/bin/env bash
# deploy-k8s.sh — Deploy mcp-apis to k3d using plain kubectl manifests
# Usage: bash scripts/deploy-k8s.sh [--capture-body]
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../." && pwd)"
CLUSTER_NAME="mcp-apis"
NAMESPACE="mcp-apis"
CAPTURE_BODY="false"

# ─── Parse args ────────────────────────────────────────────────────────────────
for arg in "$@"; do
  case $arg in
    --capture-body) CAPTURE_BODY="true" ;;
    *) echo "Unknown argument: $arg"; exit 1 ;;
  esac
done

# ─── Prerequisites ─────────────────────────────────────────────────────────────
echo "🔍 Checking prerequisites..."
for cmd in k3d kubectl docker; do
  if ! command -v "$cmd" &>/dev/null; then
    echo "❌ '$cmd' not found. Please install it first."
    exit 1
  fi
done
echo "✅ All prerequisites found."

# ─── k3d cluster ──────────────────────────────────────────────────────────────
if k3d cluster list --no-headers 2>/dev/null | awk '{print $1}' | grep -q "^${CLUSTER_NAME}$"; then
  echo "ℹ️  k3d cluster '${CLUSTER_NAME}' already exists — skipping creation."
else
  echo "🚀 Creating k3d cluster '${CLUSTER_NAME}'..."
  k3d cluster create "${CLUSTER_NAME}" \
    --port "8080:80@loadbalancer" \
    --port "8443:443@loadbalancer" \
    --k3s-arg "--disable=traefik@server:0"
fi

kubectl config use-context "k3d-${CLUSTER_NAME}"

# ─── Nginx ingress controller ──────────────────────────────────────────────────
echo "🌐 Installing nginx ingress controller..."
kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.10.1/deploy/static/provider/cloud/deploy.yaml

echo "⏳ Waiting for ingress controller to be ready..."
kubectl wait \
  --namespace ingress-nginx \
  --for=condition=ready pod \
  --selector=app.kubernetes.io/component=controller \
  --timeout=180s

# ─── Build + load Docker images ────────────────────────────────────────────────
echo "🐳 Building Docker images..."

docker build \
  -f "${REPO_ROOT}/src/Services/PrecoAPI/Dockerfile" \
  -t precoapi:latest \
  "${REPO_ROOT}"

docker build \
  -f "${REPO_ROOT}/src/Services/ProdutoAPI/Dockerfile" \
  -t produtoapi:latest \
  "${REPO_ROOT}"

docker build \
  -f "${REPO_ROOT}/src/Services/McpServer/Dockerfile" \
  -t mcpserver:latest \
  "${REPO_ROOT}"

echo "📦 Loading images into k3d cluster..."
k3d image import precoapi:latest produtoapi:latest mcpserver:latest --cluster "${CLUSTER_NAME}"

# ─── Apply manifests (ordered) ─────────────────────────────────────────────────
echo "📋 Applying Kubernetes manifests..."

kubectl apply -f "${REPO_ROOT}/k8s/namespace.yaml"
kubectl apply -f "${REPO_ROOT}/k8s/postgres-produto/"
kubectl apply -f "${REPO_ROOT}/k8s/postgres-preco/"
kubectl apply -f "${REPO_ROOT}/k8s/jaeger/"
kubectl apply -f "${REPO_ROOT}/k8s/prometheus/"
kubectl apply -f "${REPO_ROOT}/k8s/loki/"
kubectl apply -f "${REPO_ROOT}/k8s/promtail/"
kubectl apply -f "${REPO_ROOT}/k8s/grafana/"
kubectl apply -f "${REPO_ROOT}/k8s/mcpserver/"
kubectl apply -f "${REPO_ROOT}/k8s/precoapi/"
kubectl apply -f "${REPO_ROOT}/k8s/produtoapi/"

if [[ "$CAPTURE_BODY" == "true" ]]; then
  kubectl patch configmap precoapi-config   -n "${NAMESPACE}" --type merge -p '{"data":{"Otel__CaptureBody":"true"}}'
  kubectl patch configmap produtoapi-config -n "${NAMESPACE}" --type merge -p '{"data":{"Otel__CaptureBody":"true"}}'
fi

# ─── Wait for rollouts ─────────────────────────────────────────────────────────
echo "⏳ Waiting for rollouts..."

kubectl rollout status statefulset/postgres-produto -n "${NAMESPACE}" --timeout=120s
kubectl rollout status statefulset/postgres-preco   -n "${NAMESPACE}" --timeout=120s
kubectl rollout status deployment/jaeger            -n "${NAMESPACE}" --timeout=120s
kubectl rollout status deployment/prometheus        -n "${NAMESPACE}" --timeout=120s
kubectl rollout status deployment/loki              -n "${NAMESPACE}" --timeout=120s
kubectl rollout status deployment/grafana           -n "${NAMESPACE}" --timeout=120s
kubectl rollout status deployment/mcpserver         -n "${NAMESPACE}" --timeout=120s
kubectl rollout status deployment/precoapi          -n "${NAMESPACE}" --timeout=120s
kubectl rollout status deployment/produtoapi        -n "${NAMESPACE}" --timeout=120s

# ─── Done ──────────────────────────────────────────────────────────────────────
cat <<EOF

✅ Deploy complete!

� Run the port-forward script to access the services (no hosts file needed):
   bash scripts/port-forward.sh

   Then open:
   PrecoAPI   → http://localhost:5001/api/prices
   PrecoAPI   → http://localhost:5001/scalar/v1
   ProdutoAPI → http://localhost:5002/api/products
   ProdutoAPI → http://localhost:5002/scalar/v1
   Jaeger     → http://localhost:16686
   Prometheus → http://localhost:9090
   Grafana    → http://localhost:3000  (admin/admin)
   MCP Server → http://localhost:4000/sse  (SSE transport)

EOF
