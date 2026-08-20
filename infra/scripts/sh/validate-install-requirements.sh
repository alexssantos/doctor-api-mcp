#!/usr/bin/env bash
set -Eeuo pipefail

PHASE="installer"
NAMESPACE="mcp-apis"
RELEASE="doctor-api-mcp"
SCOPE="Cluster"
SERVICE_DISCOVERY="true"
STATE_STORAGE="ConfigMap"
DEPLOYMENT_EVENTS="true"
NETWORK_POLICY="false"
INGRESS="false"
PDB="true"
CONTEXT=""
PASS=0
FAIL=0

usage() {
  cat <<'EOF'
Usage: validate-install-requirements.sh [options]

  --phase installer|runtime|all
  --namespace NAME
  --release NAME
  --scope Cluster|Namespace|None
  --service-discovery true|false
  --state-storage ConfigMap|Memory
  --deployment-events true|false
  --network-policy true|false
  --ingress true|false
  --pdb true|false
  --context KUBECTL_CONTEXT
EOF
}

while [[ $# -gt 0 ]]; do
  case "$1" in
    --phase) PHASE="$2"; shift 2 ;;
    --namespace) NAMESPACE="$2"; shift 2 ;;
    --release) RELEASE="$2"; shift 2 ;;
    --scope) SCOPE="$2"; shift 2 ;;
    --service-discovery) SERVICE_DISCOVERY="$2"; shift 2 ;;
    --state-storage) STATE_STORAGE="$2"; shift 2 ;;
    --deployment-events) DEPLOYMENT_EVENTS="$2"; shift 2 ;;
    --network-policy) NETWORK_POLICY="$2"; shift 2 ;;
    --ingress) INGRESS="$2"; shift 2 ;;
    --pdb) PDB="$2"; shift 2 ;;
    --context) CONTEXT="$2"; shift 2 ;;
    -h|--help) usage; exit 0 ;;
    *) printf '[FAIL] Unknown argument: %s\n' "$1" >&2; usage; exit 2 ;;
  esac
done

SCOPE="$(printf '%s' "${SCOPE}" | tr '[:upper:]' '[:lower:]')"
SERVICE_DISCOVERY="$(printf '%s' "${SERVICE_DISCOVERY}" | tr '[:upper:]' '[:lower:]')"
STATE_STORAGE="$(printf '%s' "${STATE_STORAGE}" | tr '[:upper:]' '[:lower:]')"
DEPLOYMENT_EVENTS="$(printf '%s' "${DEPLOYMENT_EVENTS}" | tr '[:upper:]' '[:lower:]')"
NETWORK_POLICY="$(printf '%s' "${NETWORK_POLICY}" | tr '[:upper:]' '[:lower:]')"
INGRESS="$(printf '%s' "${INGRESS}" | tr '[:upper:]' '[:lower:]')"
PDB="$(printf '%s' "${PDB}" | tr '[:upper:]' '[:lower:]')"

if [[ ! "${PHASE}" =~ ^(installer|runtime|all)$ ]]; then
  printf '[FAIL] Invalid phase: %s\n' "${PHASE}" >&2
  exit 2
fi
if [[ ! "${SCOPE}" =~ ^(cluster|namespace|none)$ ]]; then
  printf '[FAIL] Invalid scope: %s\n' "${SCOPE}" >&2
  exit 2
fi
if [[ ! "${STATE_STORAGE}" =~ ^(configmap|memory)$ ]]; then
  printf '[FAIL] Invalid state storage: %s\n' "${STATE_STORAGE}" >&2
  exit 2
fi

KUBECTL=(kubectl)
[[ -n "${CONTEXT}" ]] && KUBECTL+=(--context "${CONTEXT}")

pass() { printf '  [PASS] %s\n' "$1"; PASS=$((PASS + 1)); }
fail() { printf '  [FAIL] %s\n' "$1"; FAIL=$((FAIL + 1)); }
section() { printf '\n=== %s ===\n' "$1"; }

can_i() {
  local expected="$1" label="$2"
  shift 2
  local actual
  actual="$("${KUBECTL[@]}" auth can-i "$@" 2>/dev/null || true)"
  if [[ "${actual}" == "${expected}" ]]; then
    pass "${label} -> ${expected}"
  else
    fail "${label} -> expected ${expected}, got ${actual:-no-result}"
  fi
}

installer_requirements() {
  section "Installer permissions"
  if "${KUBECTL[@]}" version --request-timeout=5s >/dev/null 2>&1; then
    pass 'Kubernetes API is reachable'
  else
    fail 'Kubernetes API is reachable'
    return
  fi

  if "${KUBECTL[@]}" get namespace "${NAMESPACE}" >/dev/null 2>&1; then
    pass "namespace/${NAMESPACE} already exists"
  else
    can_i yes "installer can create namespace/${NAMESPACE}" create namespaces
  fi

  local resources=(
    serviceaccounts
    configmaps
    secrets
    services
    deployments.apps
  )
  if [[ "${SCOPE}" == "namespace" || "${STATE_STORAGE}" == "configmap" ]]; then
    resources+=(
      roles.rbac.authorization.k8s.io
      rolebindings.rbac.authorization.k8s.io
    )
  fi
  [[ "${PDB}" == "true" ]] && resources+=(poddisruptionbudgets.policy)
  [[ "${NETWORK_POLICY}" == "true" ]] && resources+=(networkpolicies.networking.k8s.io)
  [[ "${INGRESS}" == "true" ]] && resources+=(ingresses.networking.k8s.io)

  local resource verb
  for resource in "${resources[@]}"; do
    for verb in get create update patch delete; do
      can_i yes "installer can ${verb} ${resource}" "${verb}" "${resource}" -n "${NAMESPACE}"
    done
  done
  can_i yes 'installer can list Helm release secrets' list secrets -n "${NAMESPACE}"
  for verb in list watch; do
    can_i yes "installer can ${verb} deployments.apps" \
      "${verb}" deployments.apps -n "${NAMESPACE}"
  done
  for verb in get list watch create delete; do
    can_i yes "installer can ${verb} pods" "${verb}" pods -n "${NAMESPACE}"
  done

  if [[ "${SCOPE}" == "cluster" ]]; then
    for resource in clusterroles.rbac.authorization.k8s.io clusterrolebindings.rbac.authorization.k8s.io; do
      for verb in get create update patch delete; do
        can_i yes "cluster mode installer can ${verb} ${resource}" "${verb}" "${resource}"
      done
    done
  else
    pass "${SCOPE} mode requires no cluster-scoped object creation"
  fi
}

runtime_requirements() {
  section "Runtime service-account permissions"
  local deployment service_account state_configmap identity
  deployment="$("${KUBECTL[@]}" get deployment -n "${NAMESPACE}" \
    -l "app.kubernetes.io/instance=${RELEASE}" \
    -o jsonpath='{.items[0].metadata.name}' 2>/dev/null || true)"
  if [[ -z "${deployment}" ]]; then
    fail "deployment for Helm release ${RELEASE} exists"
    return
  fi
  pass "deployment/${deployment} found"

  service_account="$("${KUBECTL[@]}" get deployment "${deployment}" -n "${NAMESPACE}" \
    -o jsonpath='{.spec.template.spec.serviceAccountName}' 2>/dev/null || true)"
  if [[ -z "${service_account}" ]]; then
    fail 'runtime ServiceAccount is configured'
    return
  fi
  pass "runtime ServiceAccount is ${service_account}"
  identity="system:serviceaccount:${NAMESPACE}:${service_account}"

  runtime_can_i() {
    local expected="$1" label="$2"
    shift 2
    can_i "${expected}" "${label}" --as="${identity}" "$@"
  }

  if [[ "${SCOPE}" == "none" ]]; then
    runtime_can_i no 'Scope None cannot list pods' list pods -n "${NAMESPACE}"
    runtime_can_i no 'Scope None cannot list services' list services -n "${NAMESPACE}"
    local automount
    automount="$("${KUBECTL[@]}" get deployment "${deployment}" -n "${NAMESPACE}" \
      -o jsonpath='{.spec.template.spec.automountServiceAccountToken}' 2>/dev/null || true)"
    [[ "${automount}" == "false" ]] &&
      pass 'Scope None disables ServiceAccount token automount' ||
      fail "Scope None disables ServiceAccount token automount (got ${automount:-unset})"
    return
  fi

  local scope_args=(-n "${NAMESPACE}")
  [[ "${SCOPE}" == "cluster" ]] && scope_args=(--all-namespaces)
  runtime_can_i yes 'runtime can list pods in declared scope' list pods "${scope_args[@]}"
  runtime_can_i yes 'runtime can get pods in declared scope' get pods "${scope_args[@]}"
  runtime_can_i yes 'runtime can list deployments in declared scope' list deployments.apps "${scope_args[@]}"
  runtime_can_i yes 'runtime can get deployments in declared scope' get deployments.apps "${scope_args[@]}"

  if [[ "${DEPLOYMENT_EVENTS}" == "true" ]]; then
    runtime_can_i yes 'runtime can list events in declared scope' list events "${scope_args[@]}"
    runtime_can_i yes 'runtime can get events in declared scope' get events "${scope_args[@]}"
  fi

  if [[ "${SERVICE_DISCOVERY}" == "true" ]]; then
    runtime_can_i yes 'runtime can list services in declared scope' list services "${scope_args[@]}"
    runtime_can_i yes 'runtime can get services in declared scope' get services "${scope_args[@]}"
    runtime_can_i yes 'runtime can list endpoints in declared scope' list endpoints "${scope_args[@]}"
    runtime_can_i yes 'runtime can get endpoints in declared scope' get endpoints "${scope_args[@]}"
  else
    runtime_can_i no 'service discovery disabled: cannot list services' list services -n "${NAMESPACE}"
    runtime_can_i no 'service discovery disabled: cannot list endpoints' list endpoints -n "${NAMESPACE}"
  fi

  if [[ "${SCOPE}" == "namespace" ]]; then
    runtime_can_i no 'namespace mode cannot list pods cluster-wide' list pods --all-namespaces
    runtime_can_i no 'namespace mode cannot list services cluster-wide' list services --all-namespaces
  fi

  if [[ "${STATE_STORAGE}" == "configmap" ]]; then
    state_configmap="$("${KUBECTL[@]}" get configmap "${deployment}-config" -n "${NAMESPACE}" \
      -o jsonpath='{.data.Discovery__StateConfigMap}' 2>/dev/null || true)"
    [[ -z "${state_configmap}" ]] && state_configmap="${deployment}-state"
    runtime_can_i yes 'runtime can get its state ConfigMap' \
      get "configmap/${state_configmap}" -n "${NAMESPACE}"
    runtime_can_i yes 'runtime can update its state ConfigMap' \
      update "configmap/${state_configmap}" -n "${NAMESPACE}"
    runtime_can_i yes 'runtime can patch its state ConfigMap' \
      patch "configmap/${state_configmap}" -n "${NAMESPACE}"
  else
    pass 'memory state requires no ConfigMap permission'
  fi
}

case "${PHASE}" in
  installer) installer_requirements ;;
  runtime) runtime_requirements ;;
  all) installer_requirements; runtime_requirements ;;
esac

printf '\nINSTALL_REQUIREMENTS_SUMMARY:phase=%s:pass=%d:fail=%d\n' \
  "${PHASE}" "${PASS}" "${FAIL}"
[[ "${FAIL}" -eq 0 ]]
