import { useQuery } from '@tanstack/react-query'

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
