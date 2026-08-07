#!/usr/bin/env bash
# deploy-k8s.sh — Deploy mcp-apis to k3d via kubectl raw manifests
# Usage: bash infra/scripts/sh/deploy-k8s.sh [--capture-body]
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../../." && pwd)"
CLUSTER_NAME="mcp-apis"
NAMESPACE="mcp-apis"
K3S_IMAGE="rancher/k3s:v1.36.1-k3s1"
CAPTURE_BODY="false"
RELEASE_BLOCKER=false

# ─── Parse args ────────────────────────────────────────────────────────────────
for arg in "$@"; do
  case $arg in
    --capture-body) CAPTURE_BODY="true" ;;
    *) echo "Unknown argument: $arg"; exit 1 ;;
  esac
done

# ─── Prerequisites ─────────────────────────────────────────────────────────────
echo "[CHK] Checking prerequisites..."
for cmd in k3d kubectl docker; do
  if ! command -v "$cmd" &>/dev/null; then
    echo "[FAIL] '$cmd' not found. Please install it first."
    exit 1
  fi
done
echo "[OK]  All prerequisites found."

# ─── k3d cluster ──────────────────────────────────────────────────────────────
if k3d cluster list --no-headers 2>/dev/null | awk '{print $1}' | grep -q "^${CLUSTER_NAME}$"; then
  echo "INFO  k3d cluster '${CLUSTER_NAME}' already exists — skipping creation."
else
  echo ">>> Creating k3d cluster '${CLUSTER_NAME}'..."
  k3d cluster create "${CLUSTER_NAME}" \
    --image "${K3S_IMAGE}" \
    --port "8080:80@loadbalancer" \
    --port "8443:443@loadbalancer" \
    --k3s-arg "--prefer-bundled-bin@server:0"
fi

kubectl config use-context "k3d-${CLUSTER_NAME}"

SERVER_VERSION="$(kubectl get node -o jsonpath='{.items[0].status.nodeInfo.kubeletVersion}' 2>/dev/null || true)"
if [[ "$SERVER_VERSION" =~ ^v1\.([0-9]+) ]] && (( BASH_REMATCH[1] < 36 )); then
  RELEASE_BLOCKER=true
  echo "[WARN] Cluster existing uses Kubernetes ${SERVER_VERSION}; NetworkPolicy was validated only on K3s 1.36.1+."
  echo "[WARN] Preserve data and recreate the cluster before the Phase 7 release gate."
elif [[ ! "$SERVER_VERSION" =~ ^v[0-9]+\.[0-9]+ ]]; then
  RELEASE_BLOCKER=true
  echo "[WARN] Could not identify the Kubernetes version; validate NetworkPolicy before release."
else
  echo "[OK]  Kubernetes version compatible with the NetworkPolicy gate: ${SERVER_VERSION}"
fi

# ─── K3s built-in Traefik ──────────────────────────────────────────────────────
echo "[NET] Waiting for the K3s built-in Traefik controller..."
if ! kubectl get deployment traefik -n kube-system >/dev/null 2>&1; then
  if kubectl get deployment ingress-nginx-controller -n ingress-nginx >/dev/null 2>&1; then
    echo "[FAIL] This legacy cluster was created with Traefik disabled. Preserve its data and recreate it; current manifests no longer use Ingress-NGINX."
    exit 1
  fi
  kubectl wait --for=create deployment/traefik -n kube-system --timeout=180s
fi
kubectl wait --for=condition=Available deployment/traefik -n kube-system --timeout=180s
TRAEFIK_IMAGE="$(kubectl get deployment traefik -n kube-system -o jsonpath='{.spec.template.spec.containers[0].image}')"
echo "[OK]  K3s built-in Traefik ready: ${TRAEFIK_IMAGE}"

# ─── Build + load Docker images ────────────────────────────────────────────────
echo "[IMG] Building Docker images..."

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

echo "[PKG] Loading images into k3d cluster..."
k3d image import precoapi:latest produtoapi:latest mcpserver:latest --cluster "${CLUSTER_NAME}"

# ─── Apply manifests (ordered) ─────────────────────────────────────────────────
echo "[LIST] Applying Kubernetes manifests..."

kubectl apply -f "${REPO_ROOT}/infra/k8s/namespace.yaml"
kubectl apply -f "${REPO_ROOT}/infra/k8s/banco/postgres-produto/"
kubectl apply -f "${REPO_ROOT}/infra/k8s/banco/postgres-preco/"
kubectl apply -f "${REPO_ROOT}/infra/k8s/observabilidade/jaeger/"
kubectl apply -f "${REPO_ROOT}/infra/k8s/observabilidade/prometheus/"
kubectl apply -f "${REPO_ROOT}/infra/k8s/observabilidade/loki/"
kubectl apply -f "${REPO_ROOT}/infra/k8s/observabilidade/promtail/"
kubectl apply -f "${REPO_ROOT}/infra/k8s/observabilidade/grafana/"
kubectl apply -k "${REPO_ROOT}/infra/k8s/overlays/k3d/"
kubectl apply -f "${REPO_ROOT}/infra/k8s/aplicacao/precoapi/"
kubectl apply -f "${REPO_ROOT}/infra/k8s/aplicacao/produtoapi/"

# Importing a :latest image does not replace containers that are already
# running. Restart all local application deployments so this run validates the
# images built immediately above.
kubectl rollout restart deployment/precoapi deployment/produtoapi deployment/mcpserver -n "${NAMESPACE}"

if [[ "$CAPTURE_BODY" == "true" ]]; then
  kubectl patch configmap precoapi-config   -n "${NAMESPACE}" --type merge -p '{"data":{"Otel__CaptureBody":"true"}}'
  kubectl patch configmap produtoapi-config -n "${NAMESPACE}" --type merge -p '{"data":{"Otel__CaptureBody":"true"}}'
fi

# ─── Wait for rollouts ─────────────────────────────────────────────────────────
echo "... Waiting for rollouts..."

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
if $RELEASE_BLOCKER; then
  PHASE7_DEPLOY_MESSAGE="[WARN] Deploy operational, but the Phase 7 release gate is BLOCKED by the Kubernetes runtime."
else
  PHASE7_DEPLOY_MESSAGE="[OK]  Deploy complete. Run validate-phase7.sh before treating it as release-ready."
fi

cat <<EOF

${PHASE7_DEPLOY_MESSAGE}

Run the port-forward script to access the services (no hosts file needed):
   bash infra/scripts/sh/port-forward.sh

   Then open:
   PrecoAPI   → http://localhost:5001/api/prices
   PrecoAPI   → http://localhost:5001/scalar/v1
   ProdutoAPI → http://localhost:5002/api/products
   ProdutoAPI → http://localhost:5002/scalar/v1
   Jaeger     → http://localhost:16686
   Prometheus → http://localhost:9090
   Grafana    → http://localhost:3000  (credential stored in the local Secret)
   MCP Server → http://localhost:4000/  (Streamable HTTP)

EOF
