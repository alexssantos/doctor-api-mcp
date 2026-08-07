import type {
  ClusterSummary,
  DashboardLinks,
  DiscoveredApplication,
  HealthState,
  ServiceHealthSummary,
} from '@/lib/api'

export const mockCluster: ClusterSummary = {
  totalPods: 18,
  readyPods: 17,
  totalDeployments: 6,
  readyDeployments: 6,
}

function mockHealth(name: string, state: HealthState, score: number): ServiceHealthSummary {
  return {
    service: {
      serviceName: name,
      namespace: 'mcp-apis',
      deploymentName: name,
      kubernetesServiceName: name,
      otelServiceName: name,
      metricsId: name,
      aliases: [name],
    },
    healthStatus: state,
    score,
    coverage: 1,
    criticalFindings: state === 'critical' ? 1 : 0,
    evaluatedAt: '2026-08-05T09:00:00Z',
  }
}

function mockApplication(
  input: Pick<DiscoveredApplication, 'name' | 'sources'> & Partial<DiscoveredApplication>,
): DiscoveredApplication {
  const { name, sources, ...overrides } = input
  return {
    name,
    namespace: 'mcp-apis',
    sources,
    deploymentName: name,
    kubernetesServiceName: name,
    otelServiceName: name,
    baseUrl: `http://${name}.mcp-apis.svc:8080`,
    selector: { app: name },
    image: `${name}:1.4.0`,
    imageDigest: null,
    version: '1.4.0',
    revision: '4',
    desiredReplicas: 2,
    readyReplicas: 2,
    owner: 'platform',
    team: 'core-services',
    description: null,
    coverage: {
      kubernetes: 'available',
      metrics: 'available',
      traces: 'available',
      logs: 'available',
      openApi: 'available',
      events: 'available',
    },
    declaredDependencies: [],
    hasReadyEndpoints: true,
    openApi: { validated: true, path: '/swagger/v1/swagger.json', failures: [] },
    enabled: true,
    lockedDisabled: false,
    firstSeen: '2026-06-02T10:00:00Z',
    lastSeen: '2026-08-05T09:00:00Z',
    missing: false,
    health: mockHealth(name, 'healthy', 96),
    ...overrides,
  }
}

export const mockApplications: DiscoveredApplication[] = [
  mockApplication({
    name: 'catalog-api',
    sources: ['deployment', 'network', 'otel'],
    desiredReplicas: 3,
    readyReplicas: 3,
  }),
  mockApplication({
    name: 'orders-api',
    sources: ['deployment', 'network', 'otel', 'config'],
    readyReplicas: 1,
    health: mockHealth('orders-api', 'critical', 42),
    declaredDependencies: ['payments-api'],
  }),
  mockApplication({
    name: 'payments-api',
    sources: ['deployment', 'otel'],
    enabled: false,
    openApi: {
      validated: false,
      path: '/swagger/v1/swagger.json',
      failures: ['Falha ao validar $ref de PaymentIntent'],
    },
    coverage: {
      kubernetes: 'available',
      metrics: 'unavailable',
      traces: 'available',
      logs: 'unavailable',
      openApi: 'unavailable',
      events: 'available',
    },
  }),
  mockApplication({
    name: 'notifications-worker',
    sources: ['otel'],
    deploymentName: null,
    kubernetesServiceName: null,
    baseUrl: null,
    selector: {},
    image: null,
    version: null,
    revision: null,
    desiredReplicas: 0,
    readyReplicas: 0,
    hasReadyEndpoints: false,
    openApi: { validated: false, path: null, failures: [] },
    health: null,
  }),
  mockApplication({
    name: 'gateway',
    sources: ['deployment', 'network', 'otel', 'config'],
    lockedDisabled: true,
    desiredReplicas: 3,
    readyReplicas: 3,
  }),
  mockApplication({
    name: 'legacy-billing',
    sources: ['network'],
    deploymentName: null,
    otelServiceName: null,
    baseUrl: null,
    image: null,
    version: null,
    revision: null,
    desiredReplicas: 0,
    readyReplicas: 0,
    enabled: false,
    hasReadyEndpoints: false,
    openApi: { validated: false, path: null, failures: [] },
    missing: true,
    health: null,
  }),
]

export const mockEnabledCount = mockApplications.filter((application) => application.enabled).length
export const mockLinks: DashboardLinks = {}
