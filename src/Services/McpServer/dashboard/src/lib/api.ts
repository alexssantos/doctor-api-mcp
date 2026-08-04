import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

// ---------------------------------------------------------------------------
// Types (mirrors McpApis.McpServer.Api.DashboardEndpoints DTOs)
// ---------------------------------------------------------------------------

export interface PodHealth {
  name: string
  phase: string
  ready: boolean
  restarts: number
  containerStates: string[]
}

export interface ServiceHealth {
  service: string
  podCount: number
  allReady: boolean
  pods: PodHealth[]
}

export interface ServiceOverview {
  name: string
  baseUrl: string
  openApiPath: string
  health: ServiceHealth | null
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
  services: ServiceOverview[]
  cluster: ClusterSummary
  links: DashboardLinks
}

export interface TraceSummary {
  traceId: string
  rootOperation: string
  spanCount: number
  durationMs: number
}

export interface DependencyEdge {
  parent?: string
  child?: string
  callCount?: number
}

export interface PrometheusVectorResult {
  status: string
  data?: {
    resultType: string
    result: Array<{
      metric: Record<string, string>
      value?: [number, string]
      values?: Array<[number, string]>
    }>
  }
}

// Feature 004: auto-discovered application inventory + indexing toggle
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
  hasReadyEndpoints: boolean
  openApi: OpenApiInfo
  enabled: boolean
  lockedDisabled: boolean
  firstSeen: string
  lastSeen: string
  missing: boolean
  health: ServiceHealth | null
}

export interface ApplicationsResponse {
  generatedAt: string
  lastScanAt: string | null
  applications: DiscoveredApplication[]
}

export interface IndexingResponse {
  name: string
  enabled: boolean
  persisted: boolean
}

// ---------------------------------------------------------------------------
// Fetch helpers
// ---------------------------------------------------------------------------

async function getJson<T>(url: string): Promise<T> {
  const response = await fetch(url)
  if (!response.ok) {
    const body = await response.text().catch(() => '')
    throw new Error(`${response.status} ${response.statusText}: ${body}`)
  }
  return (await response.json()) as T
}

async function sendJson<T>(url: string, method: 'PUT' | 'POST', body?: unknown): Promise<T> {
  const response = await fetch(url, {
    method,
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

// ---------------------------------------------------------------------------
// React Query hooks
// ---------------------------------------------------------------------------

export function useOverview() {
  return useQuery({
    queryKey: ['overview'],
    queryFn: () => getJson<OverviewResponse>(`${API_BASE}/overview`),
  })
}

export function useTraces(service?: string, limit = 15) {
  return useQuery({
    queryKey: ['traces', service, limit],
    queryFn: () => getJson<TraceSummary[]>(`${API_BASE}/traces?service=${encodeURIComponent(service!)}&limit=${limit}`),
    enabled: Boolean(service),
    retry: 0,
  })
}

export function useDependencies() {
  return useQuery({
    queryKey: ['dependencies'],
    queryFn: () => getJson<DependencyEdge[]>(`${API_BASE}/dependencies`),
    retry: 0,
  })
}

export function useMetricsRange(query?: string, minutes = 30, step = '15s') {
  return useQuery({
    queryKey: ['metrics-range', query, minutes, step],
    queryFn: () =>
      getJson<PrometheusVectorResult>(
        `${API_BASE}/metrics/range?query=${encodeURIComponent(query!)}&minutes=${minutes}&step=${step}`,
      ),
    enabled: Boolean(query),
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
    mutationFn: ({ name, enabled }: { name: string; enabled: boolean }) =>
      sendJson<IndexingResponse>(
        `${API_BASE}/applications/${encodeURIComponent(name)}/indexing`,
        'PUT',
        { enabled },
      ),
    // Optimistic update: flip the switch immediately, roll back on error.
    onMutate: async ({ name, enabled }) => {
      await queryClient.cancelQueries({ queryKey: ['applications'] })
      const previous = queryClient.getQueryData<ApplicationsResponse>(['applications'])
      if (previous) {
        queryClient.setQueryData<ApplicationsResponse>(['applications'], {
          ...previous,
          applications: previous.applications.map((app) =>
            app.name === name ? { ...app, enabled } : app,
          ),
        })
      }
      return { previous }
    },
    onError: (_error, _variables, context) => {
      if (context?.previous) {
        queryClient.setQueryData(['applications'], context.previous)
      }
    },
    onSettled: () => {
      queryClient.invalidateQueries({ queryKey: ['applications'] })
      queryClient.invalidateQueries({ queryKey: ['overview'] })
    },
  })
}

export function useRescanDiscovery() {
  const queryClient = useQueryClient()
  return useMutation({
    mutationFn: () => sendJson<{ status: string }>(`${API_BASE}/discovery/rescan`, 'POST'),
    onSettled: () => {
      // The scan runs async in the backend; give it a moment before refreshing.
      setTimeout(() => {
        queryClient.invalidateQueries({ queryKey: ['applications'] })
        queryClient.invalidateQueries({ queryKey: ['overview'] })
      }, 2000)
    },
  })
}
