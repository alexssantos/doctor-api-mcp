#!/usr/bin/env bash
set -euo pipefail

LOCAL_BIN="$HOME/.local/bin"
mkdir -p "$LOCAL_BIN"

echo "=== Installing kubectl ==="
KUBECTL_VERSION=$(curl -sL https://dl.k8s.io/release/stable.txt)
curl -sLo "$LOCAL_BIN/kubectl" "https://dl.k8s.io/release/${KUBECTL_VERSION}/bin/linux/amd64/kubectl"
chmod +x "$LOCAL_BIN/kubectl"
echo "kubectl: $("$LOCAL_BIN/kubectl" version --client 2>&1 | head -1)"

echo "=== Installing helm ==="
HELM_TMP=$(mktemp -d)
curl -sL https://get.helm.sh/helm-v3.17.3-linux-amd64.tar.gz | tar -xz -C "$HELM_TMP"
cp "$HELM_TMP/linux-amd64/helm" "$LOCAL_BIN/helm"
chmod +x "$LOCAL_BIN/helm"
rm -rf "$HELM_TMP"
echo "helm: $("$LOCAL_BIN/helm" version --short)"

echo "=== Verifying k3d ==="
"$LOCAL_BIN/k3d" version

echo ""
echo "All tools installed to $LOCAL_BIN"
echo "Add to PATH permanently — appending to ~/.bashrc..."

# Add to PATH if not already there
if ! grep -q 'LOCAL_BIN' ~/.bashrc 2>/dev/null; then
  echo 'export PATH="$HOME/.local/bin:$PATH"' >> ~/.bashrc
  echo "Added to ~/.bashrc"
else
  echo "Already in ~/.bashrc"
fi

echo "Done."
