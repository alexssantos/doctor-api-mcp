#!/usr/bin/env bash
# sync-kubeconfig.sh — Sincroniza kubeconfigs k3d do WSL para o Windows/Lens.
# Necessario porque k3d nao consegue atomic-rename cross-device (WSL ext4 -> NTFS).
#
# Uso:
#   bash sync-kubeconfig.sh <cluster-name>
#   bash sync-kubeconfig.sh --all
set -euo pipefail

export PATH="$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin"

REQUESTED_CLUSTER="${1:---all}"
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

WIN_KUBE_DIR="$(dirname "$WIN_KUBE")"
mkdir -p "$WIN_KUBE_DIR"

WORK_DIR="$(mktemp -d)"
trap 'rm -rf "$WORK_DIR"' EXIT

if [[ "$REQUESTED_CLUSTER" == "--all" ]]; then
  mapfile -t CLUSTERS < <(
    k3d cluster list --no-headers 2>/dev/null |
      awk 'NF > 0 { print $1 }' |
      sort -u
  )
else
  CLUSTERS=("$REQUESTED_CLUSTER")
fi

if [[ ${#CLUSTERS[@]} -eq 0 ]]; then
  echo "WARN: nenhum cluster k3d encontrado no WSL." >&2
  exit 0
fi

CURRENT_CONTEXT=""
if [[ -s "$WIN_KUBE" ]]; then
  CURRENT_CONTEXT="$(kubectl --kubeconfig "$WIN_KUBE" config current-context 2>/dev/null || true)"
fi

KUBECONFIG_SOURCES=()
SYNCED_CLUSTERS=()
for CLUSTER in "${CLUSTERS[@]}"; do
  RAW_CONFIG="$WORK_DIR/${CLUSTER}.raw.yaml"
  LENS_CONFIG="$WORK_DIR/${CLUSTER}.yaml"

  if ! k3d kubeconfig get "$CLUSTER" > "$RAW_CONFIG" 2>/dev/null || [[ ! -s "$RAW_CONFIG" ]]; then
    echo "WARN: kubeconfig vazio ou indisponivel para cluster '$CLUSTER'." >&2
    continue
  fi

  # k3d publica o API server em todas as interfaces. Para clientes Windows,
  # inclusive Lens, 127.0.0.1 e um destino conectavel; 0.0.0.0 nao e.
  sed 's#https://0\.0\.0\.0:#https://127.0.0.1:#g' "$RAW_CONFIG" > "$LENS_CONFIG"
  kubectl --kubeconfig "$LENS_CONFIG" config view --raw >/dev/null

  KUBECONFIG_SOURCES+=("$LENS_CONFIG")
  SYNCED_CLUSTERS+=("$CLUSTER")
done

if [[ ${#KUBECONFIG_SOURCES[@]} -eq 0 ]]; then
  echo "FAIL: nenhum kubeconfig k3d valido foi gerado." >&2
  exit 1
fi

if [[ -s "$WIN_KUBE" ]]; then
  # Os configs novos vem primeiro para substituir entradas k3d com o mesmo
  # nome; contextos externos (por exemplo AKS) continuam no arquivo Windows.
  KUBECONFIG_SOURCES+=("$WIN_KUBE")
fi

MERGED_CONFIG="$WORK_DIR/config.merged.yaml"
KUBECONFIG_VALUE="$(IFS=:; echo "${KUBECONFIG_SOURCES[*]}")"
KUBECONFIG="$KUBECONFIG_VALUE" kubectl config view --flatten --raw > "$MERGED_CONFIG"

if [[ ! -s "$MERGED_CONFIG" ]]; then
  echo "FAIL: o kubeconfig consolidado ficou vazio." >&2
  exit 1
fi

# Evita que a ordem alfabetica dos clusters altere o contexto em uso.
if [[ -n "$CURRENT_CONTEXT" ]] &&
   kubectl --kubeconfig "$MERGED_CONFIG" config get-contexts -o name 2>/dev/null |
     grep -Fxq "$CURRENT_CONTEXT"; then
  kubectl --kubeconfig "$MERGED_CONFIG" config use-context "$CURRENT_CONTEXT" >/dev/null
fi

kubectl --kubeconfig "$MERGED_CONFIG" config view --raw >/dev/null

if [[ "$REQUESTED_CLUSTER" == "--all" && -s "$WIN_KUBE" ]]; then
  BACKUP_DIR="$WIN_KUBE_DIR/backups"
  mkdir -p "$BACKUP_DIR"
  BACKUP_PATH="$BACKUP_DIR/config.pre-lens-wsl.$(date +%Y%m%d_%H%M%S).yaml"
  cp "$WIN_KUBE" "$BACKUP_PATH"
  echo "Backup criado: $BACKUP_PATH"
fi

WINDOWS_TMP="$(mktemp "$WIN_KUBE_DIR/config.lens.tmp.XXXXXX")"
cp "$MERGED_CONFIG" "$WINDOWS_TMP"
mv -f "$WINDOWS_TMP" "$WIN_KUBE"

echo "Kubeconfig sincronizado: $WIN_KUBE"
printf 'Clusters k3d registrados para Lens:'
printf ' %s' "${SYNCED_CLUSTERS[@]}"
printf '\n'
