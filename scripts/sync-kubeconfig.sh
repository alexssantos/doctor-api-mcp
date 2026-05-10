#!/usr/bin/env bash
# sync-kubeconfig.sh — merge WSL k3d kubeconfig into Windows ~/.kube/config
# Lens and other Windows tools will automatically pick up the cluster.
# Usage: bash scripts/sync-kubeconfig.sh [cluster-name]
set -euo pipefail

export PATH="$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin"

CLUSTER_NAME="${1:-mcp-apis}"

# Detect Windows username — exclude known system folders, pick first with AppData/Local
SYSTEM_DIRS='All Users|AppData|Default|Default User|Public|TEMP|Todos os Usuários|Usuário Padrão|UMFD|desktop.ini'
WIN_USER=$(ls /mnt/c/Users/ 2>/dev/null \
  | grep -vE "^(${SYSTEM_DIRS})" \
  | grep -v '\.Font Driver Host' \
  | while IFS= read -r d; do
      [[ -d "/mnt/c/Users/$d/AppData/Local" ]] && echo "$d" && break
    done)
WIN_KUBE="/mnt/c/Users/${WIN_USER}/.kube/config"
mkdir -p "$(dirname "$WIN_KUBE")"

echo "🔄 Syncing kubeconfig for 'k3d-${CLUSTER_NAME}' → ${WIN_KUBE}..."

# Export cluster kubeconfig from k3d
k3d kubeconfig get "${CLUSTER_NAME}" > /tmp/k3d-${CLUSTER_NAME}.yaml

# Merge with existing Windows kubeconfig (or just copy if none exists)
if [[ -f "$WIN_KUBE" ]]; then
  KUBECONFIG="${WIN_KUBE}:/tmp/k3d-${CLUSTER_NAME}.yaml" \
    kubectl config view --flatten > /tmp/merged-kube.yaml
  mv /tmp/merged-kube.yaml "$WIN_KUBE"
else
  cp /tmp/k3d-${CLUSTER_NAME}.yaml "$WIN_KUBE"
fi

# Clean up temp file
rm -f /tmp/k3d-${CLUSTER_NAME}.yaml

echo "✅ Done. Cluster 'k3d-${CLUSTER_NAME}' is now in the Windows kubeconfig."
echo "   Lens will reflect it on the next refresh (or restart Lens if already open)."
