#!/usr/bin/env bash
# setup-kubeconfig-link.sh — One-time setup: symlink WSL ~/.kube/config → Windows path
#
# After this, every k3d/kubectl write in WSL is instantly visible to Lens (and
# any other Windows tool) without running any sync script.
#
# Usage: bash scripts/setup-kubeconfig-link.sh
set -euo pipefail

export PATH="$HOME/.local/bin:/usr/local/bin:/usr/bin:/bin"

# ─── Detect Windows user path ─────────────────────────────────────────────────
SYSTEM_DIRS='All Users|AppData|Default|Default User|Public|TEMP|Todos os Usuários|Usuário Padrão|UMFD|desktop.ini'
WIN_USER=$(ls /mnt/c/Users/ 2>/dev/null \
  | grep -vE "^(${SYSTEM_DIRS})" \
  | grep -v '\.Font Driver Host' \
  | while IFS= read -r d; do
      [[ -d "/mnt/c/Users/$d/AppData/Local" ]] && echo "$d" && break
    done)

if [[ -z "$WIN_USER" ]]; then
  echo "[FAIL] Could not detect Windows username under /mnt/c/Users/."
  exit 1
fi

WIN_KUBE_DIR="/mnt/c/Users/${WIN_USER}/.kube"
WIN_KUBE="${WIN_KUBE_DIR}/config"
WSL_KUBE="$HOME/.kube/config"

echo "Windows user : ${WIN_USER}"
echo "Windows path : ${WIN_KUBE}"
echo "WSL path     : ${WSL_KUBE}"
echo ""

# ─── Already correctly linked? ────────────────────────────────────────────────
if [[ -L "$WSL_KUBE" ]]; then
  CURRENT_TARGET=$(readlink "$WSL_KUBE")
  if [[ "$CURRENT_TARGET" == "$WIN_KUBE" ]]; then
    echo "[OK]  Symlink already in place: ${WSL_KUBE} → ${WIN_KUBE}"
    exit 0
  fi
  echo "[WARN]  ~/.kube/config is a symlink pointing to: ${CURRENT_TARGET}"
  read -rp "   Replace it? [y/N] " reply
  [[ "$reply" =~ ^[Yy]$ ]] || { echo "Aborted."; exit 1; }
  rm "$WSL_KUBE"
fi

# ─── Ensure Windows .kube dir exists ──────────────────────────────────────────
mkdir -p "$WIN_KUBE_DIR"
mkdir -p "$HOME/.kube"

# ─── Migrate existing WSL kubeconfig into Windows path (one-time) ─────────────
if [[ -f "$WSL_KUBE" ]]; then
  echo "🔄 Migrating existing WSL kubeconfig into Windows path..."
  if [[ -f "$WIN_KUBE" ]]; then
    KUBECONFIG="${WIN_KUBE}:${WSL_KUBE}" kubectl config view --flatten > /tmp/kube-merged.yaml
    mv /tmp/kube-merged.yaml "$WIN_KUBE"
  else
    cp "$WSL_KUBE" "$WIN_KUBE"
  fi
  rm "$WSL_KUBE"
fi

# If Windows path doesn't exist yet, create an empty placeholder so the symlink
# target exists before any cluster is deployed.
if [[ ! -f "$WIN_KUBE" ]]; then
  touch "$WIN_KUBE"
fi

# ─── Create the symlink ────────────────────────────────────────────────────────
ln -sf "$WIN_KUBE" "$WSL_KUBE"

echo ""
echo "[OK]  Done!  ${WSL_KUBE}"
echo "          └─ symlink → ${WIN_KUBE}"
echo ""
echo "   From now on every k3d/kubectl operation in WSL writes directly to the"
echo "   Windows kubeconfig. Lens will reflect new clusters immediately."
