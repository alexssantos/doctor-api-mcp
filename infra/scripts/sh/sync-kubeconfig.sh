#!/usr/bin/env bash
# sync-kubeconfig.sh — Sincroniza o kubeconfig do cluster k3d para o Windows.
# Necessario porque k3d nao consegue atomic-rename cross-device (WSL ext4 -> NTFS).
#
# Uso: bash sync-kubeconfig.sh <cluster-name>
set -euo pipefail

export PATH="$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin"

CLUSTER="${1:-mcp-apis}"
WIN_KUBE="/mnt/c/Users/$(cmd.exe /c "echo %USERNAME%" 2>/dev/null | tr -d '\r\n'  || echo "arkan")/.kube/config"

# Detectar caminho do Windows automaticamente se cmd falhar
if [[ ! -d "$(dirname "$WIN_KUBE")" ]]; then
  WIN_USER=$(ls /mnt/c/Users/ 2>/dev/null \
    | grep -vE "^(All Users|AppData|Default|Public|TEMP|desktop\.ini)" \
    | while IFS= read -r d; do
        [[ -d "/mnt/c/Users/$d/AppData/Local" ]] && echo "$d" && break
      done)
  WIN_KUBE="/mnt/c/Users/${WIN_USER}/.kube/config"
fi

TMPCONFIG=$(mktemp)
trap 'rm -f "$TMPCONFIG" "${TMPCONFIG}.merged"' EXIT

k3d kubeconfig get "$CLUSTER" > "$TMPCONFIG" 2>/dev/null

if [[ ! -s "$TMPCONFIG" ]]; then
  echo "WARN: kubeconfig vazio para cluster '$CLUSTER'" >&2
  exit 0
fi

mkdir -p "$(dirname "$WIN_KUBE")"

if [[ -s "$WIN_KUBE" ]]; then
  # Mesclar com kubeconfig existente preservando outros clusters
  KUBECONFIG="${WIN_KUBE}:${TMPCONFIG}" kubectl config view --flatten > "${TMPCONFIG}.merged" 2>/dev/null
  if [[ -s "${TMPCONFIG}.merged" ]]; then
    cat "${TMPCONFIG}.merged" > "$WIN_KUBE"
  else
    cat "$TMPCONFIG" > "$WIN_KUBE"
  fi
else
  cat "$TMPCONFIG" > "$WIN_KUBE"
fi

echo "Kubeconfig sincronizado: $WIN_KUBE"
