#!/usr/bin/env bash
set -Eeuo pipefail

REPOSITORY="${DOCTOR_API_MCP_REPOSITORY:-https://github.com/alexssantos/doctor-api-mcp}"
REF="${DOCTOR_API_MCP_REF:-master}"
RELEASE="${DOCTOR_API_MCP_RELEASE:-doctor-api-mcp}"
NAMESPACE="${DOCTOR_API_MCP_NAMESPACE:-mcp-apis}"

for command_name in curl tar helm kubectl; do
  if ! command -v "${command_name}" >/dev/null 2>&1; then
    echo "[erro] '${command_name}' não foi encontrado no PATH." >&2
    exit 1
  fi
done

TEMP_DIR="$(mktemp -d -t doctor-api-mcp.XXXXXXXX)"
cleanup() {
  case "${TEMP_DIR}" in
    "${TMPDIR:-/tmp}"/doctor-api-mcp.*|/tmp/doctor-api-mcp.*) rm -rf -- "${TEMP_DIR}" ;;
    *) echo "[aviso] diretório temporário inesperado; limpeza ignorada: ${TEMP_DIR}" >&2 ;;
  esac
}
trap cleanup EXIT

ARCHIVE="${TEMP_DIR}/source.tar.gz"
echo "[1/3] Baixando doctor-api-mcp (${REF})..."
curl --fail --silent --show-error --location \
  "${REPOSITORY}/archive/refs/heads/${REF}.tar.gz" \
  --output "${ARCHIVE}"

tar --extract --gzip --file "${ARCHIVE}" --directory "${TEMP_DIR}"
CHART_DIR="$(find "${TEMP_DIR}" -mindepth 1 -maxdepth 1 -type d -print -quit)/infra/helm/doctor-api-mcp"
if [[ ! -f "${CHART_DIR}/Chart.yaml" ]]; then
  echo "[erro] Chart Helm não encontrado no pacote baixado." >&2
  exit 1
fi

echo "[2/3] Instalando release '${RELEASE}' no namespace '${NAMESPACE}'..."
helm upgrade --install "${RELEASE}" "${CHART_DIR}" \
  --namespace "${NAMESPACE}" \
  --create-namespace \
  --wait \
  --timeout 5m \
  "$@"

echo "[3/3] Validando o rollout..."
kubectl rollout status "deployment/${RELEASE}" \
  --namespace "${NAMESPACE}" \
  --timeout=180s

cat <<EOF

doctor-api-mcp instalado.

Abra o acesso local:
  kubectl port-forward service/${RELEASE} 4000:4000 -n ${NAMESPACE}

Dashboard: http://localhost:4000/dashboard
MCP:       http://localhost:4000/
EOF
