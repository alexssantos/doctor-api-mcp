#!/usr/bin/env bash
set -Eeuo pipefail

REPO_ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../../.." && pwd)"
CHART="${REPO_ROOT}/infra/helm/doctor-api-mcp"
SCENARIOS="${REPO_ROOT}/tests/cluster-lab/scenarios"
NAMESPACE="mcp-install-test"
WORK_DIR="$(mktemp -d -t mcp-install-scenarios.XXXXXXXX)"
PASS=0
FAIL=0

cleanup() {
  case "${WORK_DIR}" in
    /tmp/mcp-install-scenarios.*) rm -rf -- "${WORK_DIR}" ;;
    *) printf '[WARN] refusing to remove unexpected path: %s\n' "${WORK_DIR}" >&2 ;;
  esac
}
trap cleanup EXIT

pass() { printf '  [PASS] %s\n' "$1"; PASS=$((PASS + 1)); }
fail() { printf '  [FAIL] %s\n' "$1"; FAIL=$((FAIL + 1)); }

assert_contains() {
  local file="$1" pattern="$2" label="$3"
  if grep -Eq -- "${pattern}" "${file}"; then pass "${label}"; else fail "${label}"; fi
}

assert_not_contains() {
  local file="$1" pattern="$2" label="$3"
  if grep -Eq -- "${pattern}" "${file}"; then fail "${label}"; else pass "${label}"; fi
}

render() {
  local scenario="$1"
  helm lint "${CHART}" --namespace "${NAMESPACE}" \
    -f "${SCENARIOS}/${scenario}.yaml" \
    --set-string security.allowedNamespaces[0]="${NAMESPACE}" >/dev/null
  helm template doctor-api-mcp "${CHART}" \
    --namespace "${NAMESPACE}" \
    -f "${SCENARIOS}/${scenario}.yaml" \
    --set-string security.allowedNamespaces[0]="${NAMESPACE}" \
    --set-string services.fixture="http://fixture.${NAMESPACE}.svc.cluster.local" \
    >"${WORK_DIR}/${scenario}.yaml"
  pass "${scenario}: lint and render"
}

for scenario in cluster namespace-only no-volumes no-service-discovery restricted; do
  render "${scenario}"
done

assert_contains "${WORK_DIR}/cluster.yaml" '^kind: ClusterRole$' \
  'cluster: ClusterRole is rendered'
assert_contains "${WORK_DIR}/cluster.yaml" '^      - services$' \
  'cluster: service discovery RBAC is rendered'

assert_not_contains "${WORK_DIR}/namespace-only.yaml" '^kind: ClusterRole$' \
  'namespace-only: no cluster-scoped RBAC'
assert_contains "${WORK_DIR}/namespace-only.yaml" '^kind: Role$' \
  'namespace-only: namespaced Role is rendered'
assert_contains "${WORK_DIR}/namespace-only.yaml" 'ClusterAccess__Scope: "Namespace"' \
  'namespace-only: runtime receives Namespace scope'

helm template doctor-api-mcp "${CHART}" --namespace "${NAMESPACE}" \
  -f "${SCENARIOS}/no-volumes.yaml" --show-only templates/deployment.yaml \
  >"${WORK_DIR}/no-volumes-deployment.yaml"
assert_not_contains "${WORK_DIR}/no-volumes-deployment.yaml" 'volumeMounts:|^      volumes:' \
  'no-volumes: MCP declares no writable volumes'
assert_not_contains "${WORK_DIR}/no-volumes-deployment.yaml" 'name: TMPDIR' \
  'no-volumes: TMPDIR volume contract is absent'

helm template doctor-api-mcp "${CHART}" --namespace "${NAMESPACE}" \
  -f "${SCENARIOS}/no-service-discovery.yaml" \
  --set-string security.allowedNamespaces[0]="${NAMESPACE}" \
  --set-string services.fixture="http://fixture.${NAMESPACE}.svc.cluster.local" \
  --show-only templates/rbac.yaml >"${WORK_DIR}/no-discovery-rbac.yaml"
assert_not_contains "${WORK_DIR}/no-discovery-rbac.yaml" '^      - services$|^      - endpoints$' \
  'no-service-discovery: Service and Endpoints RBAC is absent'
assert_contains "${WORK_DIR}/no-service-discovery.yaml" 'Discovery__Mode: "Config"' \
  'no-service-discovery: discovery is forced to Config'
assert_contains "${WORK_DIR}/no-service-discovery.yaml" 'Services__fixture:' \
  'no-service-discovery: explicit endpoint is rendered'

assert_not_contains "${WORK_DIR}/restricted.yaml" '^kind: (ClusterRole|ClusterRoleBinding|Role|RoleBinding)$' \
  'restricted: no runtime RBAC is rendered'
assert_not_contains "${WORK_DIR}/restricted.yaml" 'name: .*state$|indexing-overrides:' \
  'restricted: state ConfigMap is absent'
assert_contains "${WORK_DIR}/restricted.yaml" 'automountServiceAccountToken: false' \
  'restricted: Kubernetes token automount is disabled'
helm template doctor-api-mcp "${CHART}" --namespace "${NAMESPACE}" \
  -f "${SCENARIOS}/restricted.yaml" \
  --set-string services.fixture="http://fixture.${NAMESPACE}.svc.cluster.local" \
  --show-only templates/deployment.yaml >"${WORK_DIR}/restricted-deployment.yaml"
assert_not_contains "${WORK_DIR}/restricted-deployment.yaml" 'volumeMounts:|^      volumes:' \
  'restricted: MCP declares no volumes'

if helm template invalid "${CHART}" --namespace "${NAMESPACE}" \
    --set clusterAccess.scope=None >/dev/null 2>&1; then
  fail 'invalid: Scope None with cluster defaults must be rejected'
else
  pass 'invalid: Scope None with cluster defaults is rejected'
fi

if helm template invalid "${CHART}" --namespace "${NAMESPACE}" \
    --set clusterAccess.serviceDiscovery=false >/dev/null 2>&1; then
  fail 'invalid: disabled discovery without explicit services must be rejected'
else
  pass 'invalid: disabled discovery without explicit services is rejected'
fi

if helm template invalid "${CHART}" --namespace "${NAMESPACE}" \
    --set clusterAccess.stateStorage=Memory >/dev/null 2>&1; then
  fail 'invalid: memory state with multiple replicas must be rejected'
else
  pass 'invalid: memory state with multiple replicas is rejected'
fi

if helm template invalid "${CHART}" --namespace "${NAMESPACE}" \
    -f "${SCENARIOS}/restricted.yaml" \
    --set serviceAccount.automountToken=true >/dev/null 2>&1; then
  fail 'invalid: Scope None with an API token must be rejected'
else
  pass 'invalid: Scope None with an API token is rejected'
fi

printf '\nINSTALL_SCENARIO_SUMMARY:pass=%d:fail=%d\n' "${PASS}" "${FAIL}"
[[ "${FAIL}" -eq 0 ]]
