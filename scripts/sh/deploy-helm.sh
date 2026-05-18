#!/usr/bin/env bash
# deploy-helm.sh — Deploy mcp-apis to k3d usando Helm (modo PADRAO)
# Usage: bash scripts/sh/deploy-helm.sh [--capture-body] [--image-tag <tag>]
#
# Para deploy via kubectl raw manifests, use: bash scripts/sh/deploy-k8s.sh
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../." && pwd)"
CLUSTER_NAME="mcp-apis"
NAMESPACE="mcp-apis"
CAPTURE_BODY="false"
IMAGE_TAG="latest"

# ─── Parse args ────────────────────────────────────────────────────────────────
while [[ $# -gt 0 ]]; do
  case $1 in
    --capture-body)   CAPTURE_BODY="true"; shift ;;
    --image-tag)      IMAGE_TAG="$2"; shift 2 ;;
    *) echo "Unknown argument: $1"; exit 1 ;;
  esac
done

# ─── Prerequisites ─────────────────────────────────────────────────────────────
echo "🔍 Checking prerequisites..."
for cmd in k3d kubectl docker helm; do
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

# ─── Namespace ─────────────────────────────────────────────────────────────────
kubectl apply -f "${REPO_ROOT}/k8s/namespace.yaml"

# ─── Nginx ingress controller ──────────────────────────────────────────────────
echo "🌐 Installing nginx ingress controller..."
kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.10.1/deploy/static/provider/cloud/deploy.yaml

echo "⏳ Waiting for ingress controller to be ready..."
kubectl wait \
  --namespace ingress-nginx \
  --for=condition=ready pod \
  --selector=app.kubernetes.io/component=controller \
  --timeout=180s

# ─── Bitnami Helm repo ─────────────────────────────────────────────────────────
echo "📚 Adding Bitnami Helm repo..."
helm repo add bitnami https://charts.bitnami.com/bitnami || true
helm repo update

# ─── PostgreSQL instances ──────────────────────────────────────────────────────
echo "🐘 Installing PostgreSQL for ProdutoDB..."
helm upgrade --install postgres-produto bitnami/postgresql \
  --namespace "${NAMESPACE}" \
  --set auth.username=postgres \
  --set auth.password=postgres \
  --set auth.database=produto_db \
  --wait --timeout 120s

echo "🐘 Installing PostgreSQL for PrecoDB..."
helm upgrade --install postgres-preco bitnami/postgresql \
  --namespace "${NAMESPACE}" \
  --set auth.username=postgres \
  --set auth.password=postgres \
  --set auth.database=preco_db \
  --wait --timeout 120s

# Map bitnami service names: bitnami/postgresql uses <release>-postgresql
POSTGRES_PRODUTO_HOST="postgres-produto-postgresql"
POSTGRES_PRECO_HOST="postgres-preco-postgresql"

# ─── Jaeger (raw manifest — no dedicated Helm chart needed) ────────────────────
echo "🔭 Installing Jaeger..."
kubectl apply -f "${REPO_ROOT}/k8s/jaeger/deployment.yaml"
kubectl apply -f "${REPO_ROOT}/k8s/jaeger/service.yaml"
kubectl rollout status deployment/jaeger -n "${NAMESPACE}" --timeout=120s
# ─── Observability stack (Prometheus + Loki + Promtail + Grafana) ────────
echo "📊 Installing observability stack..."
kubectl apply -f "${REPO_ROOT}/k8s/prometheus/"
kubectl apply -f "${REPO_ROOT}/k8s/loki/"
kubectl apply -f "${REPO_ROOT}/k8s/promtail/"
kubectl apply -f "${REPO_ROOT}/k8s/grafana/"
kubectl rollout status deployment/prometheus -n "${NAMESPACE}" --timeout=120s
kubectl rollout status deployment/loki       -n "${NAMESPACE}" --timeout=120s
kubectl rollout status deployment/grafana    -n "${NAMESPACE}" --timeout=120s
# ─── Build + load Docker images ────────────────────────────────────────────────
echo "🐳 Building Docker images (tag: ${IMAGE_TAG})..."

docker build \
  -f "${REPO_ROOT}/src/Services/PrecoAPI/Dockerfile" \
  -t "precoapi:${IMAGE_TAG}" \
  "${REPO_ROOT}"

docker build \
  -f "${REPO_ROOT}/src/Services/ProdutoAPI/Dockerfile" \
  -t "produtoapi:${IMAGE_TAG}" \
  "${REPO_ROOT}"

docker build \
  -f "${REPO_ROOT}/src/Services/McpServer/Dockerfile" \
  -t "mcpserver:${IMAGE_TAG}" \
  "${REPO_ROOT}"

echo "📦 Loading images into k3d cluster..."
k3d image import "precoapi:${IMAGE_TAG}" "produtoapi:${IMAGE_TAG}" "mcpserver:${IMAGE_TAG}" --cluster "${CLUSTER_NAME}"

# ─── Helm install: PrecoAPI ────────────────────────────────────────────────────
echo "⚙️  Installing PrecoAPI Helm chart..."
helm upgrade --install precoapi "${REPO_ROOT}/helm/precoapi" \
  --namespace "${NAMESPACE}" \
  --set image.tag="${IMAGE_TAG}" \
  --set db.host="${POSTGRES_PRECO_HOST}" \
  --set otel.captureBody="${CAPTURE_BODY}" \
  --wait --timeout 120s

# ─── Helm install: ProdutoAPI ─────────────────────────────────────────────────
echo "⚙️  Installing ProdutoAPI Helm chart..."
helm upgrade --install produtoapi "${REPO_ROOT}/helm/produtoapi" \
  --namespace "${NAMESPACE}" \
  --set image.tag="${IMAGE_TAG}" \
  --set db.host="${POSTGRES_PRODUTO_HOST}" \
  --set otel.captureBody="${CAPTURE_BODY}" \
  --wait --timeout 120s

# ─── Helm install: MCP Server ──────────────────────────────────────────────────
echo "⚙️  Installing MCP Server Helm chart..."
helm upgrade --install mcpserver "${REPO_ROOT}/helm/mcpserver" \
  --namespace "${NAMESPACE}" \
  --set image.tag="${IMAGE_TAG}" \
  --wait --timeout 120s

# ─── Status ────────────────────────────────────────────────────────────────────
echo ""
echo "📊 Helm releases:"
helm list -n "${NAMESPACE}"

# ─── Done ──────────────────────────────────────────────────────────────────────
cat <<EOF

✅ Helm deploy complete!

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

🔄 To upgrade (e.g. after code change):
   bash scripts/deploy-helm.sh --image-tag v1.1.0

EOF
