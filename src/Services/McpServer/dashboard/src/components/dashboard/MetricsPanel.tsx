import { useMemo, useState } from 'react'
import { AlertCircle, Gauge } from 'lucide-react'
import { CartesianGrid, Line, LineChart, ResponsiveContainer, Tooltip as RechartsTooltip, XAxis, YAxis } from 'recharts'

import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Select } from '@/components/ui/select'
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

export function MetricsPanel({ service }: { service?: string }) {
  const [presetKey, setPresetKey] = useState<keyof typeof PRESETS>('up')
  const [customQuery, setCustomQuery] = useState('')

  const query = useMemo(() => {
    if (!service && presetKey !== 'custom') return undefined
    if (presetKey === 'custom') return customQuery || undefined
    return PRESETS[presetKey].query(service!)
  }, [presetKey, service, customQuery])

  const { data, isLoading, isError, isFetching } = useMetricsRange(query, 30, '15s')
  const chartData = useMemo(() => toChartData(data), [data])

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <Gauge className="size-4 text-chart-3" />
          Métricas (Prometheus)
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        <div className="flex flex-col gap-2 sm:flex-row">
          <Select
            value={presetKey}
            onChange={(e) => setPresetKey(e.target.value as keyof typeof PRESETS)}
            className="sm:w-64"
          >
            {Object.entries(PRESETS).map(([key, preset]) => (
              <option key={key} value={key}>
                {preset.label}
              </option>
            ))}
          </Select>

          {presetKey === 'custom' && (
            <Input
              placeholder="ex.: rate(http_server_request_duration_seconds_count[5m])"
              value={customQuery}
              onChange={(e) => setCustomQuery(e.target.value)}
              className="font-mono text-xs"
            />
          )}
        </div>

        {!service && presetKey !== 'custom' && (
          <p className="py-8 text-center text-sm text-muted-foreground">Selecione um serviço acima para ver métricas.</p>
        )}

        {query && isLoading && <Skeleton className="h-56 w-full" />}

        {query && isError && (
          <div className="flex items-center gap-2 rounded-lg border border-border bg-muted/40 p-3 text-sm text-muted-foreground">
            <AlertCircle className="size-4 shrink-0" />
            Não foi possível consultar o Prometheus para esta métrica.
          </div>
        )}

        {query && !isLoading && !isError && chartData.length === 0 && (
          <p className="py-8 text-center text-sm text-muted-foreground">Sem dados para o intervalo selecionado.</p>
        )}

        {query && chartData.length > 0 && (
          <div className={isFetching ? 'opacity-70 transition-opacity' : 'transition-opacity'}>
            <ResponsiveContainer width="100%" height={224}>
              <LineChart data={chartData} margin={{ top: 8, right: 8, left: -16, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" className="stroke-border" />
                <XAxis dataKey="time" fontSize={11} tickLine={false} axisLine={false} />
                <YAxis fontSize={11} tickLine={false} axisLine={false} width={48} />
                <RechartsTooltip
                  contentStyle={{
                    background: 'var(--popover)',
                    border: '1px solid var(--border)',
                    borderRadius: 8,
                    fontSize: 12,
                  }}
                />
                <Line type="monotone" dataKey="value" stroke="var(--chart-3)" strokeWidth={2} dot={false} />
              </LineChart>
            </ResponsiveContainer>
          </div>
        )}
      </CardContent>
    </Card>
  )
}
