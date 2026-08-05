import { useMemo, useState } from 'react'
import { AlertCircle, Gauge, LineChart as LineChartIcon, MousePointerClick } from 'lucide-react'
import { Area, AreaChart, CartesianGrid, ResponsiveContainer, Tooltip as RechartsTooltip, XAxis, YAxis } from 'recharts'

import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { EmptyState, StaleNotice } from '@/components/ui/empty-state'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Input } from '@/components/ui/input'
import { Skeleton } from '@/components/ui/skeleton'
import { useMetricsRange } from '@/lib/api'

type Preset = {
  label: string
  query: (service: string) => string
}

const PRESETS: Record<string, Preset> = {
  up: { label: 'Disponibilidade (up)', query: () => 'up' },
  requestRate: {
    label: 'Taxa de requisições (5m)',
    query: (service) => `sum(rate(http_server_request_duration_seconds_count{service="${service}"}[5m]))`,
  },
  errorRate: {
    label: 'Taxa de erros 5xx (5m)',
    query: (service) =>
      `sum(rate(http_server_request_duration_seconds_count{service="${service}", http_response_status_code=~"5.."}[5m]))`,
  },
  memory: {
    label: 'Memória do processo (bytes)',
    query: (service) => `process_runtime_dotnet_gc_heap_size_bytes{service="${service}"}`,
  },
  custom: { label: 'Consulta PromQL personalizada', query: () => '' },
}

/** Step is tuned per range so the chart keeps roughly 100-250 points. */
const RANGES = [
  { label: 'Últimos 15 min', minutes: 15, step: '10s' },
  { label: 'Últimos 30 min', minutes: 30, step: '15s' },
  { label: 'Última 1 hora', minutes: 60, step: '30s' },
  { label: 'Últimas 6 horas', minutes: 360, step: '2m' },
] as const

interface ChartPoint {
  time: string
  value: number
}

function toChartData(result?: {
  data?: { result: Array<{ values?: Array<[number, string]>; value?: [number, string] }> }
}): ChartPoint[] {
  const series = result?.data?.result ?? []
  if (series.length === 0) return []

  const byTimestamp = new Map<number, number>()
  for (const s of series) {
    const values = s.values ?? (s.value ? [s.value] : [])
    for (const [ts, val] of values) {
      const num = Number.parseFloat(val)
      byTimestamp.set(ts, (byTimestamp.get(ts) ?? 0) + (Number.isFinite(num) ? num : 0))
    }
  }

  return Array.from(byTimestamp.entries())
    .sort(([a], [b]) => a - b)
    .map(([ts, value]) => ({
      time: new Date(ts * 1000).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit' }),
      value: Math.round(value * 1000) / 1000,
    }))
}

const prefersReducedMotion = () =>
  typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches

export function MetricsPanel({ service }: { service?: string }) {
  const [presetKey, setPresetKey] = useState<keyof typeof PRESETS>('up')
  const [customQuery, setCustomQuery] = useState('')
  const [rangeIndex, setRangeIndex] = useState(1)

  const range = RANGES[rangeIndex]

  const query = useMemo(() => {
    if (!service && presetKey !== 'custom') return undefined
    if (presetKey === 'custom') return customQuery || undefined
    return PRESETS[presetKey].query(service!)
  }, [presetKey, service, customQuery])

  const { data, isLoading, isError, isFetching, refetch } = useMetricsRange(query, range.minutes, range.step)
  const chartData = useMemo(() => toChartData(data), [data])

  // Keep the last good series visible on a failed refetch rather than claiming
  // the Prometheus query returned nothing.
  const hasData = chartData.length > 0
  const showError = isError && !hasData

  // Charts are invisible to screen readers; describe the series in words.
  const chartSummary = useMemo(() => {
    if (chartData.length === 0) return ''
    const values = chartData.map((p) => p.value)
    const min = Math.min(...values)
    const max = Math.max(...values)
    const last = values[values.length - 1]
    return `${PRESETS[presetKey].label}, ${range.label.toLowerCase()}: ${chartData.length} pontos, mínimo ${min}, máximo ${max}, valor atual ${last}.`
  }, [chartData, presetKey, range.label])

  return (
    <Card aria-busy={isLoading}>
      <CardHeader>
        <CardTitle>
          <Gauge className="size-4 text-chart-3" />
          Métricas (Prometheus)
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="flex flex-col gap-2 sm:flex-row">
          <Select value={presetKey} onValueChange={(value) => setPresetKey(value as keyof typeof PRESETS)}>
            <SelectTrigger aria-label="Métrica" className="sm:flex-1">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {Object.entries(PRESETS).map(([key, preset]) => (
                <SelectItem key={key} value={key}>
                  {preset.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>

          <Select value={String(rangeIndex)} onValueChange={(value) => setRangeIndex(Number(value))}>
            <SelectTrigger aria-label="Intervalo de tempo" className="sm:w-44">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              {RANGES.map((r, i) => (
                <SelectItem key={r.label} value={String(i)}>
                  {r.label}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>

        {presetKey === 'custom' && (
          <Input
            placeholder="ex.: rate(http_server_request_duration_seconds_count[5m])"
            value={customQuery}
            onChange={(e) => setCustomQuery(e.target.value)}
            aria-label="Consulta PromQL"
            className="font-mono text-xs"
          />
        )}

        {!service && presetKey !== 'custom' && (
          <EmptyState
            icon={MousePointerClick}
            title="Nenhum serviço selecionado"
            description="Escolha uma aplicação na aba Operação, ou use uma consulta PromQL personalizada."
          />
        )}

        {query && isLoading && <Skeleton className="h-56 w-full" />}

        {query && showError && (
          <EmptyState
            variant="error"
            icon={AlertCircle}
            title="Não foi possível consultar o Prometheus"
            description="Confira se a consulta é válida e se o Prometheus está acessível a partir do MCP Server."
            action={{ label: 'Tentar novamente', onClick: () => refetch() }}
          />
        )}

        {query && isError && hasData && <StaleNotice onRetry={() => refetch()} />}

        {query && !isLoading && !isError && !hasData && (
          <EmptyState
            icon={LineChartIcon}
            title="Sem dados no intervalo"
            description="Nenhuma amostra retornada para esta métrica. Tente um intervalo maior."
            action={{ label: 'Atualizar', onClick: () => refetch() }}
          />
        )}

        {query && hasData && (
          <figure className={isFetching ? 'opacity-70 transition-opacity' : 'transition-opacity'}>
            <figcaption className="sr-only">{chartSummary}</figcaption>
            <div role="img" aria-label={chartSummary}>
              <ResponsiveContainer width="100%" height={224}>
                <AreaChart data={chartData} margin={{ top: 8, right: 8, left: -16, bottom: 0 }}>
                  <defs>
                    <linearGradient id="metricFill" x1="0" y1="0" x2="0" y2="1">
                      <stop offset="0%" stopColor="var(--chart-3)" stopOpacity={0.35} />
                      <stop offset="100%" stopColor="var(--chart-3)" stopOpacity={0.02} />
                    </linearGradient>
                  </defs>
                  <CartesianGrid strokeDasharray="3 3" className="stroke-border" vertical={false} />
                  <XAxis dataKey="time" fontSize={11} tickLine={false} axisLine={false} minTickGap={24} />
                  <YAxis fontSize={11} tickLine={false} axisLine={false} width={48} />
                  <RechartsTooltip
                    contentStyle={{
                      background: 'var(--popover)',
                      border: '1px solid var(--border)',
                      borderRadius: 8,
                      fontSize: 12,
                    }}
                  />
                  <Area
                    type="monotone"
                    dataKey="value"
                    stroke="var(--chart-3)"
                    strokeWidth={2}
                    fill="url(#metricFill)"
                    dot={false}
                    isAnimationActive={!prefersReducedMotion()}
                  />
                </AreaChart>
              </ResponsiveContainer>
            </div>
          </figure>
        )}
      </CardContent>
    </Card>
  )
}
