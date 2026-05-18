#!/usr/bin/env bash
# Launcher — sets a clean PATH in WSL and runs deploy-helm.sh
# Called from Windows: wsl bash /mnt/c/.../scripts/wsl-run.sh [script]
export PATH="/home/$(whoami)/.local/bin:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin"

SCRIPT="${1:-deploy-helm.sh}"
REPO="/mnt/c/dev/repos/projetos/mcp-apis"

cd "$REPO"
exec bash "infra/scripts/${SCRIPT}" "${@:2}"
