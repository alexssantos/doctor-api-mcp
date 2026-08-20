#!/usr/bin/env bash
set -Eeuo pipefail

REPOSITORY="${DOCTOR_API_MCP_REPOSITORY:-https://github.com/alexssantos/doctor-api-mcp}"
REF="${DOCTOR_API_MCP_REF:-master}"
RELEASE="${DOCTOR_API_MCP_RELEASE:-doctor-api-mcp}"
NAMESPACE="${DOCTOR_API_MCP_NAMESPACE:-mcp-apis}"
MODE="${DOCTOR_API_MCP_MODE:-cluster}"

case "${MODE}" in
  cluster)
    DEFAULT_SCOPE=Cluster; DEFAULT_DISCOVERY=true; DEFAULT_STATE=ConfigMap
    DEFAULT_VOLUMES=true; DEFAULT_EVENTS=true; DEFAULT_REPLICAS=2; DEFAULT_PDB=true ;;
  namespace)
    DEFAULT_SCOPE=Namespace; DEFAULT_DISCOVERY=true; DEFAULT_STATE=ConfigMap
    DEFAULT_VOLUMES=true; DEFAULT_EVENTS=true; DEFAULT_REPLICAS=2; DEFAULT_PDB=true ;;
  no-volumes)
    DEFAULT_SCOPE=Cluster; DEFAULT_DISCOVERY=true; DEFAULT_STATE=ConfigMap
    DEFAULT_VOLUMES=false; DEFAULT_EVENTS=true; DEFAULT_REPLICAS=2; DEFAULT_PDB=true ;;
  no-service-discovery)
    DEFAULT_SCOPE=Namespace; DEFAULT_DISCOVERY=false; DEFAULT_STATE=ConfigMap
    DEFAULT_VOLUMES=true; DEFAULT_EVENTS=true; DEFAULT_REPLICAS=2; DEFAULT_PDB=true ;;
  restricted)
    DEFAULT_SCOPE=None; DEFAULT_DISCOVERY=false; DEFAULT_STATE=Memory
    DEFAULT_VOLUMES=false; DEFAULT_EVENTS=false; DEFAULT_REPLICAS=1; DEFAULT_PDB=false ;;
  *)
    echo "[erro] DOCTOR_API_MCP_MODE deve ser cluster, namespace, no-volumes, no-service-discovery ou restricted." >&2
    exit 2 ;;
esac

ACCESS_SCOPE="${DOCTOR_API_MCP_ACCESS_SCOPE:-${DEFAULT_SCOPE}}"
SERVICE_DISCOVERY="${DOCTOR_API_MCP_SERVICE_DISCOVERY:-${DEFAULT_DISCOVERY}}"
STATE_STORAGE="${DOCTOR_API_MCP_STATE_STORAGE:-${DEFAULT_STATE}}"
ALLOW_VOLUMES="${DOCTOR_API_MCP_ALLOW_VOLUMES:-${DEFAULT_VOLUMES}}"
DEPLOYMENT_EVENTS="${DOCTOR_API_MCP_DEPLOYMENT_EVENTS:-${DEFAULT_EVENTS}}"
REPLICAS="${DOCTOR_API_MCP_REPLICAS:-${DEFAULT_REPLICAS}}"
PDB="${DOCTOR_API_MCP_PDB:-${DEFAULT_PDB}}"
RUN_PREFLIGHT="${DOCTOR_API_MCP_PREFLIGHT:-true}"
SERVICE_NAME="${DOCTOR_API_MCP_SERVICE_NAME:-}"
SERVICE_URL="${DOCTOR_API_MCP_SERVICE_URL:-}"

if [[ "${SERVICE_DISCOVERY,,}" == "false" ]]; then
  if [[ -z "${SERVICE_NAME}" || -z "${SERVICE_URL}" ]]; then
    echo "[erro] Modos sem service discovery exigem DOCTOR_API_MCP_SERVICE_NAME e DOCTOR_API_MCP_SERVICE_URL." >&2
    exit 2
  fi
  if [[ ! "${SERVICE_NAME}" =~ ^[A-Za-z0-9_]+$ ]]; then
    echo "[erro] DOCTOR_API_MCP_SERVICE_NAME aceita apenas letras, números e underscore." >&2
    exit 2
  fi
fi

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
echo "[1/5] Baixando doctor-api-mcp (${REF})..."
curl --fail --silent --show-error --location \
  "${REPOSITORY}/archive/refs/heads/${REF}.tar.gz" \
  --output "${ARCHIVE}"

tar --extract --gzip --file "${ARCHIVE}" --directory "${TEMP_DIR}"
CHART_DIR="$(find "${TEMP_DIR}" -mindepth 1 -maxdepth 1 -type d -print -quit)/infra/helm/doctor-api-mcp"
if [[ ! -f "${CHART_DIR}/Chart.yaml" ]]; then
  echo "[erro] Chart Helm não encontrado no pacote baixado." >&2
  exit 1
fi

PREFLIGHT_SCRIPT="${CHART_DIR}/../../scripts/sh/validate-install-requirements.sh"
if [[ "${RUN_PREFLIGHT,,}" == "true" ]]; then
  echo "[2/5] Validando permissões mínimas do instalador (modo: ${MODE})..."
  bash "${PREFLIGHT_SCRIPT}" \
    --phase installer --namespace "${NAMESPACE}" --release "${RELEASE}" \
    --scope "${ACCESS_SCOPE}" --service-discovery "${SERVICE_DISCOVERY}" \
    --state-storage "${STATE_STORAGE}" --deployment-events "${DEPLOYMENT_EVENTS}" \
    --pdb "${PDB}"
else
  echo "[2/5] Preflight desabilitado por DOCTOR_API_MCP_PREFLIGHT=false."
fi

HELM_MODE_ARGS=(
  --set-string "clusterAccess.scope=${ACCESS_SCOPE}"
  --set "clusterAccess.serviceDiscovery=${SERVICE_DISCOVERY}"
  --set-string "clusterAccess.stateStorage=${STATE_STORAGE}"
  --set "clusterAccess.allowVolumes=${ALLOW_VOLUMES}"
  --set "observability.enableDeploymentEvents=${DEPLOYMENT_EVENTS}"
  --set "replicaCount=${REPLICAS}"
  --set "pdb.enabled=${PDB}"
)
if [[ "${SERVICE_DISCOVERY,,}" == "false" ]]; then
  HELM_MODE_ARGS+=(--set-string "services.${SERVICE_NAME}=${SERVICE_URL}")
fi

echo "[3/5] Instalando release '${RELEASE}' no namespace '${NAMESPACE}'..."
helm upgrade --install "${RELEASE}" "${CHART_DIR}" \
  --namespace "${NAMESPACE}" \
  --create-namespace \
  --wait \
  --timeout 5m \
  "${HELM_MODE_ARGS[@]}" \
  "$@"

echo "[4/5] Validando o rollout..."
DEPLOYMENT="$(kubectl get deployment -n "${NAMESPACE}" \
  -l "app.kubernetes.io/instance=${RELEASE}" \
  -o jsonpath='{.items[0].metadata.name}')"
kubectl rollout status "deployment/${DEPLOYMENT}" \
  --namespace "${NAMESPACE}" \
  --timeout=180s

echo "[5/5] Validando requisitos efetivos e readiness dentro do cluster..."
helm test "${RELEASE}" --namespace "${NAMESPACE}" --timeout 2m

SERVICE_RESOURCE="$(kubectl get service -n "${NAMESPACE}" \
  -l "app.kubernetes.io/instance=${RELEASE}" \
  -o jsonpath='{.items[0].metadata.name}')"

cat <<EOF

doctor-api-mcp instalado.
Modo: ${MODE}

Abra o acesso local:
  kubectl port-forward service/${SERVICE_RESOURCE} 4000:4000 -n ${NAMESPACE}

Dashboard: http://localhost:4000/dashboard
MCP:       http://localhost:4000/
EOF
