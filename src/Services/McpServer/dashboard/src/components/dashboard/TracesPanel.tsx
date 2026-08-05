import { AlertCircle, Clock, ExternalLink, GitCommitHorizontal, MousePointerClick, Waypoints } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Card, CardAction, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { EmptyState, StaleNotice } from '@/components/ui/empty-state'
import { Skeleton } from '@/components/ui/skeleton'
import { Button } from '@/components/ui/button'
import { useOverview, useTraces } from '@/lib/api'

function formatDuration(ms: number) {
  if (ms < 1) return '<1 ms'
  if (ms < 1000) return `${ms.toFixed(1)} ms`
  return `${(ms / 1000).toFixed(2)} s`
}

export function TracesPanel({ service }: { service?: string }) {
  const { data: overview } = useOverview()
  const { data: traces, isLoading, isError, refetch } = useTraces(service, 12)
  const jaegerBase = overview?.links?.jaeger ?? overview?.links?.Jaeger

  // React Query keeps the last good result on a failed refetch. Only claim
  // "no data" when there genuinely is none; otherwise flag the data as stale.
  const hasData = Boolean(traces && traces.length > 0)
  const showError = isError && !hasData

  // Bars are scaled against the slowest trace in view, so the outlier is obvious.
  const slowest = hasData ? Math.max(...traces!.map((t) => t.durationMs), 1) : 1

  return (
    <Card aria-busy={isLoading}>
      <CardHeader>
        <CardTitle>
          <Waypoints className="size-4 text-chart-1" />
          Traces recentes
          {service && <span className="truncate font-mono text-xs text-muted-foreground">· {service}</span>}
        </CardTitle>
        {jaegerBase && service && (
          <CardAction>
            <Button variant="ghost" size="sm" asChild>
              <a href={`${jaegerBase}/search?service=${encodeURIComponent(service)}`} target="_blank" rel="noreferrer">
                Abrir no Jaeger <ExternalLink className="size-3.5" />
              </a>
            </Button>
          </CardAction>
        )}
      </CardHeader>
      <CardContent className="space-y-2">
        {!service && (
          <EmptyState
            icon={MousePointerClick}
            title="Nenhum serviço selecionado"
            description="Escolha uma aplicação na aba Operação para ver os traces dela aqui."
          />
        )}

        {service && isLoading && (
          <div className="space-y-2">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-12 w-full" />
            ))}
          </div>
        )}

        {service && showError && (
          <EmptyState
            variant="error"
            icon={AlertCircle}
            title="Sem dados de trace disponíveis"
            description="O Jaeger pode ainda não ter recebido spans deste serviço."
            action={{ label: 'Tentar novamente', onClick: () => refetch() }}
          />
        )}

        {service && isError && hasData && <StaleNotice onRetry={() => refetch()} />}

        {service && !isLoading && !isError && !hasData && (
          <EmptyState
            icon={Waypoints}
            title="Nenhum trace na última hora"
            description="Gere tráfego no serviço e aguarde alguns segundos para o Jaeger processar os spans."
            action={{ label: 'Atualizar', onClick: () => refetch() }}
          />
        )}

        {service && hasData && (
          <ul className="max-h-80 space-y-1.5 overflow-y-auto scrollbar-thin pr-1">
            {traces!.map((trace) => (
              <li
                key={trace.traceId}
                className="rounded-lg border border-border/60 px-3 py-2 text-sm transition-colors hover:border-border hover:bg-accent/30"
              >
                <div className="flex items-center justify-between gap-3">
                  <div className="min-w-0 flex-1">
                    <p className="truncate font-medium">{trace.rootOperation}</p>
                    <p className="flex items-center gap-1 truncate font-mono text-xs text-muted-foreground">
                      <GitCommitHorizontal className="size-3" />
                      {trace.traceId.slice(0, 12)}
                    </p>
                  </div>
                  <div className="flex shrink-0 items-center gap-2">
                    <Badge variant="outline" className="tabular">
                      {trace.spanCount} spans
                    </Badge>
                    <Badge variant="outline" className="tabular">
                      <Clock className="size-3" />
                      {formatDuration(trace.durationMs)}
                    </Badge>
                  </div>
                </div>

                {/* Relative duration — decorative, the exact value is in the badge above. */}
                <div className="mt-1.5 h-1 w-full overflow-hidden rounded-full bg-muted" aria-hidden="true">
                  <div
                    className="h-full rounded-full bg-chart-1/70 transition-[width] duration-500"
                    style={{ width: `${Math.max(2, (trace.durationMs / slowest) * 100)}%` }}
                  />
                </div>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}
