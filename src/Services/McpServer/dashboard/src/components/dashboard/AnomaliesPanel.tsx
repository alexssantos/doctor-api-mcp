import { AlertCircle, CircleHelp, ScanSearch } from 'lucide-react'

import { ConclusionBadge, ExecutionStatusBadge, SeverityBadge } from '@/components/dashboard/StatusBadge'
import { SourceStatusStrip } from '@/components/dashboard/SourceStatusStrip'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { EmptyState, StaleNotice } from '@/components/ui/empty-state'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import type { DiscoveredApplication } from '@/lib/api'
import { useServiceAnomalies } from '@/lib/api'
import { formatDateTime, formatNumber, humanize } from '@/lib/format'

function deviation(value: number | null) {
  if (value == null) return '—'
  const percentage = value * 100
  return `${percentage > 0 ? '+' : ''}${percentage.toLocaleString('pt-BR', { maximumFractionDigits: 1 })}%`
}

export function AnomaliesPanel({ app, minutes }: { app?: DiscoveredApplication; minutes: number }) {
  const query = useServiceAnomalies(app, minutes)
  const envelope = query.data
  const report = envelope?.data

  return (
    <Card aria-busy={query.isLoading}>
      <CardHeader>
        <CardTitle>
          <ScanSearch className="text-chart-3" aria-hidden="true" />
          Anomaly Engine
          {envelope && <ExecutionStatusBadge status={envelope.executionStatus} />}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-3">
        {!app && <EmptyState icon={CircleHelp} title="Selecione um serviço para comparar baselines" />}
        {app && (!app.enabled || !app.namespace) && (
          <EmptyState
            icon={CircleHelp}
            title="Análise de anomalias indisponível"
            description="O serviço precisa estar habilitado e associado a um namespace."
          />
        )}
        {query.isLoading && !report && <Skeleton className="h-56 w-full" />}
        {query.isError && !report && (
          <EmptyState
            variant="error"
            icon={AlertCircle}
            title="Não foi possível comparar os baselines"
            description="Prometheus não respondeu à janela atual, anterior, de 24h ou de 7 dias."
            action={{ label: 'Tentar novamente', onClick: () => query.refetch() }}
          />
        )}
        {query.isError && report && <StaleNotice onRetry={() => query.refetch()} />}

        {report && (
          <>
            <div className="flex flex-wrap items-center justify-between gap-2 rounded-lg border bg-muted/20 px-3 py-2">
              <div>
                <p className="text-xs text-muted-foreground">Conclusão da janela</p>
                <p className="text-xs text-muted-foreground">robust Z-score + comparação temporal</p>
              </div>
              <ConclusionBadge conclusion={report.analysisConclusion} />
            </div>
            <SourceStatusStrip sources={envelope.sources} />
            {report.anomalies.length === 0 ? (
              <EmptyState
                icon={ScanSearch}
                title="Nenhuma anomalia detectada"
                description="Os sinais ficaram dentro dos baselines determinísticos da janela."
                className="py-6"
              />
            ) : (
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Sinal</TableHead>
                    <TableHead>Estado</TableHead>
                    <TableHead className="text-right">Atual</TableHead>
                    <TableHead className="text-right">Baseline</TableHead>
                    <TableHead className="text-right">Desvio</TableHead>
                    <TableHead>Início estimado</TableHead>
                  </TableRow>
                </TableHeader>
                <TableBody>
                  {report.anomalies.map((anomaly) => (
                    <TableRow key={anomaly.metric}>
                      <TableCell>
                        <p className="font-medium">{humanize(anomaly.metric)}</p>
                        <p className="font-mono text-[11px] text-muted-foreground">
                          {anomaly.method} · n={anomaly.sampleCount}
                        </p>
                      </TableCell>
                      <TableCell>
                        {anomaly.conclusion === 'detected' ? (
                          <SeverityBadge severity={anomaly.severity} />
                        ) : (
                          <ConclusionBadge conclusion={anomaly.conclusion} />
                        )}
                      </TableCell>
                      <TableCell className="text-right font-mono text-xs tabular">
                        {formatNumber(anomaly.currentValue, anomaly.unit)}
                      </TableCell>
                      <TableCell className="text-right font-mono text-xs tabular">
                        {formatNumber(anomaly.expectedValue, anomaly.unit)}
                      </TableCell>
                      <TableCell className="text-right font-mono text-xs tabular">
                        {deviation(anomaly.deviation)}
                      </TableCell>
                      <TableCell className="whitespace-nowrap text-xs text-muted-foreground">
                        {formatDateTime(anomaly.estimatedStart)}
                      </TableCell>
                    </TableRow>
                  ))}
                </TableBody>
              </Table>
            )}
          </>
        )}
      </CardContent>
    </Card>
  )
}
