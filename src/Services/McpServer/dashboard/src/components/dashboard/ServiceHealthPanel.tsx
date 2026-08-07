import { Activity, AlertCircle, CircleHelp, Gauge, LockKeyhole, ShieldCheck } from 'lucide-react'

import { ExecutionStatusBadge, HealthStatusBadge, SeverityBadge } from '@/components/dashboard/StatusBadge'
import { SourceStatusStrip } from '@/components/dashboard/SourceStatusStrip'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { EmptyState, StaleNotice } from '@/components/ui/empty-state'
import { Progress } from '@/components/ui/progress'
import { Skeleton } from '@/components/ui/skeleton'
import type { DiscoveredApplication } from '@/lib/api'
import { useServiceHealth } from '@/lib/api'
import { formatPercent, humanize } from '@/lib/format'
import { cn } from '@/lib/utils'

function ScoreGauge({ score, coverage }: { score: number | null; coverage: number }) {
  const value = Math.max(0, Math.min(100, score ?? 0))
  const radius = 43
  const circumference = Math.PI * radius
  const offset = circumference * (1 - value / 100)
  return (
    <div className="relative mx-auto h-28 w-52" role="img" aria-label={`Score ${score ?? 'indisponível'} de 100`}>
      <svg viewBox="0 0 120 68" className="h-full w-full overflow-visible" aria-hidden="true">
        <path
          d="M 17 58 A 43 43 0 0 1 103 58"
          pathLength={circumference}
          fill="none"
          stroke="var(--muted)"
          strokeWidth="9"
          strokeLinecap="round"
        />
        <path
          d="M 17 58 A 43 43 0 0 1 103 58"
          pathLength={circumference}
          fill="none"
          stroke="var(--primary)"
          strokeWidth="9"
          strokeLinecap="round"
          strokeDasharray={circumference}
          strokeDashoffset={offset}
          className="transition-[stroke-dashoffset] duration-500 motion-reduce:transition-none"
        />
      </svg>
      <div className="absolute inset-x-0 bottom-0 text-center">
        <p className="text-3xl font-semibold tracking-tight tabular">{score == null ? '—' : score.toFixed(0)}</p>
        <p className="text-xs text-muted-foreground">cobertura {formatPercent(coverage)}</p>
      </div>
    </div>
  )
}

export function ServiceHealthPanel({
  app,
  minutes,
}: {
  app?: DiscoveredApplication
  minutes: number
}) {
  const query = useServiceHealth(app, minutes)

  if (!app) {
    return (
      <Card>
        <CardContent className="p-5">
          <EmptyState icon={CircleHelp} title="Selecione um serviço para calcular a saúde" />
        </CardContent>
      </Card>
    )
  }
  if (!app.enabled || !app.namespace) {
    return (
      <Card>
        <CardContent className="p-5">
          <EmptyState
            icon={LockKeyhole}
            title="Inteligência indisponível para este serviço"
            description={
              !app.enabled
                ? 'Habilite a indexação no catálogo para consultar os engines.'
                : 'A aplicação ainda não foi correlacionada a um namespace Kubernetes.'
            }
          />
        </CardContent>
      </Card>
    )
  }

  const envelope = query.data
  const report = envelope?.data
  const hasData = Boolean(report)

  return (
    <Card aria-busy={query.isLoading}>
      <CardHeader>
        <CardTitle>
          <Gauge className="text-primary" aria-hidden="true" />
          Health Engine
          {envelope && <ExecutionStatusBadge status={envelope.executionStatus} />}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {query.isLoading && !hasData && (
          <div className="grid gap-4 lg:grid-cols-[13rem_1fr]">
            <Skeleton className="h-32 w-full" />
            <Skeleton className="h-32 w-full" />
          </div>
        )}
        {query.isError && !hasData && (
          <EmptyState
            variant="error"
            icon={AlertCircle}
            title="Falha ao executar o Health Engine"
            description="O último relatório não está disponível. Verifique as fontes indicadas e tente novamente."
            action={{ label: 'Tentar novamente', onClick: () => query.refetch() }}
          />
        )}
        {query.isError && hasData && <StaleNotice onRetry={() => query.refetch()} />}

        {report && (
          <>
            <div className="grid items-center gap-4 lg:grid-cols-[13rem_1fr]">
              <div className="rounded-lg border bg-muted/20 px-3 py-2">
                <div className="mb-1 flex justify-center"><HealthStatusBadge status={report.healthStatus} /></div>
                <ScoreGauge score={report.score} coverage={report.coverage} />
              </div>

              <div className="grid gap-3 sm:grid-cols-2">
                {report.dimensions.map((dimension) => (
                  <div key={dimension.name} className="rounded-lg border border-border/70 p-3">
                    <div className="flex items-center justify-between gap-3">
                      <p className="text-xs font-medium">{humanize(dimension.name)}</p>
                      <span className="font-mono text-xs tabular">
                        {dimension.score == null ? 'sem sinal' : `${dimension.score.toFixed(0)}/100`}
                      </span>
                    </div>
                    <Progress
                      value={dimension.score ?? 0}
                      className="mt-2"
                      indicatorClassName={cn(
                        dimension.score == null && 'bg-muted-foreground',
                        dimension.score != null && dimension.score < 60 && 'bg-destructive',
                        dimension.score != null && dimension.score >= 60 && dimension.score < 85 && 'bg-warning',
                        dimension.score != null && dimension.score >= 85 && 'bg-success',
                      )}
                      aria-label={`Score de ${dimension.name}`}
                    />
                    <p className="mt-1 text-[11px] text-muted-foreground">
                      peso {dimension.weight} · {dimension.required ? 'obrigatória' : 'opcional'}
                    </p>
                  </div>
                ))}
              </div>
            </div>

            <SourceStatusStrip sources={envelope.sources} />

            <section aria-labelledby="health-findings-title">
              <div className="mb-2 flex items-center justify-between gap-2">
                <h3 id="health-findings-title" className="flex items-center gap-2 text-sm font-semibold">
                  <Activity className="size-4 text-muted-foreground" aria-hidden="true" /> Findings
                </h3>
                <span className="text-xs text-muted-foreground tabular">{report.findings.length} encontrado(s)</span>
              </div>
              {report.findings.length === 0 ? (
                <div className="flex items-center gap-2 rounded-lg border border-success/30 bg-success/8 px-3 py-2 text-xs text-success-fg">
                  <ShieldCheck className="size-4" aria-hidden="true" /> Nenhum finding acima dos thresholds.
                </div>
              ) : (
                <ul className="space-y-2">
                  {report.findings.map((finding, index) => (
                    <li key={`${finding.type}-${index}`} className="rounded-lg border border-border/70 px-3 py-2.5">
                      <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                        <div>
                          <p className="text-sm font-medium">{humanize(finding.type)}</p>
                          <p className="mt-0.5 text-xs leading-relaxed text-muted-foreground">{finding.message}</p>
                        </div>
                        <SeverityBadge severity={finding.severity} />
                      </div>
                    </li>
                  ))}
                </ul>
              )}
            </section>
          </>
        )}
      </CardContent>
    </Card>
  )
}
