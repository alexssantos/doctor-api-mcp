import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

export type ExecutionStatus = 'complete' | 'partial' | 'unavailable'
export type SourceAvailability = 'available' | 'stale' | 'unavailable'
export type HealthState = 'healthy' | 'degraded' | 'critical' | 'unknown'
export type AnalysisConclusion = 'detected' | 'notDetected' | 'inconclusive'
export type FindingSeverity = 'info' | 'warning' | 'critical'

export interface ServiceIdentity {
  serviceName: string
  namespace: string
  deploymentName: string | null
  kubernetesServiceName: string | null
  otelServiceName: string | null
  metricsId: string | null
  aliases: string[]
}

export interface TimeWindow {
  from: string
  to: string
  duration: string
  timezone: string
}

export interface SourceStatus {
  name: string
  availability: SourceAvailability
  observedAt: string | null
  freshnessSeconds: number | null
  elapsedMilliseconds: number
  warnings: string[]
}

export interface Evidence {
  id: string
  source: string
  signal: string
  value: number | null
  baseline: number | null
  unit: string | null
  timestamp: string
  queryDescriptor: string
  detail: string | null
}

export interface ToolError {
  code: string
  message: string
  candidates?: string[]
  recovery?: string
}

export interface ObservationEnvelope<T> {
  schemaVersion: string
  generatedAt: string
  executionStatus: ExecutionStatus
  service: ServiceIdentity | null
  window: TimeWindow | null
  data: T | null
  sources: SourceStatus[]
  warnings: string[]
  evidence: Evidence[]
  error: ToolError | null
}

export interface Finding {
  type: string
  severity: FindingSeverity
  service: ServiceIdentity
  message: string
  startedAt: string
  evidenceIds: string[]
}

export interface HealthDimension {
  name: string
  weight: number
  score: number | null
  required: boolean
  availability: SourceAvailability
  evidenceIds: string[]
}

export interface HealthReport {
  healthStatus: HealthState
  score: number | null
  coverage: number
  dimensions: HealthDimension[]
  findings: Finding[]
  evaluatedAt: string
}

export interface ServiceHealthSummary {
  service: ServiceIdentity
  healthStatus: HealthState
  score: number | null
  coverage: number
  criticalFindings: number
  evaluatedAt: string
}

export interface SystemHealthSummary {
  healthStatus: HealthState
  totalServices: number
  healthy: number
  degraded: number
  critical: number
  unknown: number
  services: ServiceHealthSummary[]
  evaluatedAt: string
}

export interface SignalCoverage {
  kubernetes: SourceAvailability
  metrics: SourceAvailability
  traces: SourceAvailability
  logs: SourceAvailability
  openApi: SourceAvailability
  events: SourceAvailability
}

export interface ApiEndpointSummary {
  method: string
  path: string
  summary: string | null
  operationId: string | null
  responseCodes: string[]
}

export interface ServiceSpecReport {
  description: string | null
  owner: string | null
  team: string | null
  version: string | null
  image: string | null
  imageDigest: string | null
  revision: string | null
  desiredReplicas: number
  readyReplicas: number
  labels: Record<string, string>
  annotations: Record<string, string>
  selector: Record<string, string>
  coverage: SignalCoverage
  endpoints: ApiEndpointSummary[]
  declaredDependencies: string[]
}

export interface Anomaly {
  metric: string
  conclusion: AnalysisConclusion
  severity: FindingSeverity
  currentValue: number | null
  expectedValue: number | null
  deviation: number | null
  unit: string
  method: string
  sampleCount: number
  estimatedStart: string | null
  evidenceIds: string[]
}

export interface AnomalyReport {
  analysisConclusion: AnalysisConclusion
  anomalies: Anomaly[]
  evaluatedAt: string
}

export interface DependencyEdge {
  source: ServiceIdentity
  target: ServiceIdentity
  type: string
  observedAt: string
  callCount: number
  errorRate: number | null
  latencyMilliseconds: number | null
  evidenceIds: string[]
  declared: boolean
  observed: boolean
}

export interface DependencyGraph {
  root: ServiceIdentity
  depth: number
  nodes: ServiceIdentity[]
  inbound: DependencyEdge[]
  outbound: DependencyEdge[]
  cycles: string[][]
  criticalPath: string[]
  potentialBlastRadius: string[]
}

export interface IncidentEvent {
  id: string
  timestamp: string
  type: string
  service: ServiceIdentity
  severity: FindingSeverity
  source: string
  summary: string
  evidenceIds: string[]
}

export interface IncidentTimeline {
  analysisConclusion: AnalysisConclusion
  incidentStartedAt: string | null
  events: IncidentEvent[]
  correlations: string[]
}

export interface RootCauseHypothesis {
  id: string
  summary: string
  confidence: number
  supportingEvidenceIds: string[]
  contradictingEvidenceIds: string[]
  potentiallyAffectedServices: string[]
}

export interface Recommendation {
  priority: string
  action: string
  reason: string
  evidenceIds: string[]
  executable: boolean
}

export interface RootCauseReport {
  analysisConclusion: AnalysisConclusion
  primaryHypothesis: RootCauseHypothesis | null
  alternatives: RootCauseHypothesis[]
  coverage: number
  limitations: string[]
  recommendations: Recommendation[]
}

export interface ClusterSummary {
  totalPods: number
  readyPods: number
  totalDeployments: number
  readyDeployments: number
}

export interface DashboardLinks {
  [key: string]: string
}

export interface OverviewResponse {
  generatedAt: string
  window: TimeWindow
  system: SystemHealthSummary
  sources: SourceStatus[]
  warnings: string[]
  cluster: ClusterSummary
  links: DashboardLinks
}

export type DiscoverySource = 'deployment' | 'network' | 'otel' | 'config'

export interface OpenApiInfo {
  validated: boolean
  path: string | null
  failures: string[]
}

export interface DiscoveredApplication {
  name: string
  namespace: string | null
  sources: DiscoverySource[]
  deploymentName: string | null
  kubernetesServiceName: string | null
  otelServiceName: string | null
  baseUrl: string | null
  selector: Record<string, string>
  image: string | null
  imageDigest: string | null
  version: string | null
  revision: string | null
  desiredReplicas: number
  readyReplicas: number
  owner: string | null
  team: string | null
  description: string | null
  coverage: SignalCoverage
  declaredDependencies: string[]
  hasReadyEndpoints: boolean
  openApi: OpenApiInfo
  enabled: boolean
  lockedDisabled: boolean
  firstSeen: string
  lastSeen: string
  missing: boolean
  health: ServiceHealthSummary | null
}

export interface ApplicationsResponse {
  generatedAt: string
  lastScanAt: string | null
  healthWindow: TimeWindow
  sources: SourceStatus[]
  warnings: string[]
  canManage: boolean
  applications: DiscoveredApplication[]
}

export interface IndexingResponse {
  name: string
  namespace: string | null
  enabled: boolean
  persisted: boolean
}

async function getJson<T>(url: string): Promise<T> {
  const response = await fetch(url, { credentials: 'same-origin' })
  if (!response.ok) {
    const body = await response.text().catch(() => '')
    throw new Error(`${response.status} ${response.statusText}: ${body}`)
  }
  return (await response.json()) as T
}

async function sendJson<T>(url: string, method: 'PUT' | 'POST', body?: unknown): Promise<T> {
  const response = await fetch(url, {
    method,
    credentials: 'same-origin',
    headers: body !== undefined ? { 'Content-Type': 'application/json' } : undefined,
    body: body !== undefined ? JSON.stringify(body) : undefined,
  })
  if (!response.ok) {
    const text = await response.text().catch(() => '')
    throw new Error(`${response.status} ${response.statusText}: ${text}`)
  }
  return (await response.json()) as T
}

const API_BASE = '/api/dashboard'

function intelligenceUrl(
  app: Pick<DiscoveredApplication, 'name' | 'namespace'>,
  capability: string,
  params?: Record<string, string | number | undefined>,
) {
  const query = new URLSearchParams()
  if (app.namespace) query.set('namespaceName', app.namespace)
  for (const [key, value] of Object.entries(params ?? {})) {
    if (value !== undefined) query.set(key, String(value))
  }
  return `${API_BASE}/intelligence/services/${encodeURIComponent(app.name)}/${capability}?${query}`
}

export const applicationKey = (app: Pick<DiscoveredApplication, 'name' | 'namespace'>) =>
  `${app.namespace ?? '~'}/${app.name}`

export function useOverview() {
  return useQuery({
    queryKey: ['overview'],
    queryFn: () => getJson<OverviewResponse>(`${API_BASE}/overview`),
  })
}

export function useSystemHealth(minutes = 30) {
  return useQuery({
    queryKey: ['system-health', minutes],
    queryFn: () =>
      getJson<ObservationEnvelope<SystemHealthSummary>>(
        `${API_BASE}/intelligence/system?minutes=${minutes}`,
      ),
  })
}

export function useServiceHealth(app?: DiscoveredApplication, minutes = 30) {
  return useQuery({
    queryKey: ['service-health', app ? applicationKey(app) : null, minutes],
    queryFn: () =>
      getJson<ObservationEnvelope<HealthReport>>(
        intelligenceUrl(app!, 'health', { minutes }),
      ),
    enabled: Boolean(app?.namespace && app.enabled),
  })
}

export function useServiceSpec(app?: DiscoveredApplication) {
  return useQuery({
    queryKey: ['service-spec', app ? applicationKey(app) : null],
    queryFn: () =>
      getJson<ObservationEnvelope<ServiceSpecReport>>(intelligenceUrl(app!, 'spec')),
    enabled: Boolean(app?.namespace && app.enabled),
    staleTime: 5 * 60_000,
  })
}

export function useServiceDependencies(app?: DiscoveredApplication, minutes = 30, depth = 2) {
  return useQuery({
    queryKey: ['service-dependencies', app ? applicationKey(app) : null, minutes, depth],
    queryFn: () =>
      getJson<ObservationEnvelope<DependencyGraph>>(
        intelligenceUrl(app!, 'dependencies', { minutes, depth }),
      ),
    enabled: Boolean(app?.namespace && app.enabled),
    retry: 0,
  })
}

export function useServiceAnomalies(app?: DiscoveredApplication, minutes = 30) {
  return useQuery({
    queryKey: ['service-anomalies', app ? applicationKey(app) : null, minutes],
    queryFn: () =>
      getJson<ObservationEnvelope<AnomalyReport>>(
        intelligenceUrl(app!, 'anomalies', { minutes }),
      ),
    enabled: Boolean(app?.namespace && app.enabled),
    retry: 0,
  })
}

export function useIncidentTimeline(app?: DiscoveredApplication, minutes = 30) {
  return useQuery({
    queryKey: ['incident-timeline', app ? applicationKey(app) : null, minutes],
    queryFn: () =>
      getJson<ObservationEnvelope<IncidentTimeline>>(
        intelligenceUrl(app!, 'timeline', { minutes }),
      ),
    enabled: Boolean(app?.namespace && app.enabled),
    retry: 0,
  })
}

export function useRootCause(app?: DiscoveredApplication, minutes = 30, depth = 2) {
  return useQuery({
    queryKey: ['root-cause', app ? applicationKey(app) : null, minutes, depth],
    queryFn: () =>
      getJson<ObservationEnvelope<RootCauseReport>>(
        intelligenceUrl(app!, 'root-cause', { minutes, depth }),
      ),
    enabled: Boolean(app?.namespace && app.enabled),
    retry: 0,
  })
}

export function useApplications() {
  return useQuery({
    queryKey: ['applications'],
    queryFn: () => getJson<ApplicationsResponse>(`${API_BASE}/applications`),
  })
}

export function useSetIndexing() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: ({ name, namespace, enabled }: { name: string; namespace: string | null; enabled: boolean }) =>
      sendJson<IndexingResponse>(
        `${API_BASE}/applications/${encodeURIComponent(name)}/indexing`,
        'PUT',
        { enabled, namespace },
      ),
    onMutate: async ({ name, namespace, enabled }) => {
      await queryClient.cancelQueries({ queryKey: ['applications'] })
      const previous = queryClient.getQueryData<ApplicationsResponse>(['applications'])
      if (previous) {
        queryClient.setQueryData<ApplicationsResponse>(['applications'], {
          ...previous,
          applications: previous.applications.map((app) =>
            app.name === name && app.namespace === namespace ? { ...app, enabled } : app,
          ),
        })
      }
      return { previous }
    },
    onError: (_error, _variables, context) => {
      if (context?.previous) queryClient.setQueryData(['applications'], context.previous)
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ['applications'] })
      queryClient.invalidateQueries({ queryKey: ['overview'] })
      queryClient.invalidateQueries({ queryKey: ['system-health'] })
    },
  })
}

export function useRescanDiscovery() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => sendJson<{ status: string }>(`${API_BASE}/discovery/rescan`, 'POST'),
    onSettled: () => {
      setTimeout(() => {
        queryClient.invalidateQueries({ queryKey: ['applications'] })
        queryClient.invalidateQueries({ queryKey: ['overview'] })
        queryClient.invalidateQueries({ queryKey: ['system-health'] })
      }, 2000)
    },
  })
}
