import {
  AlertCircle,
  Boxes,
  CircleHelp,
  Clock3,
  FileWarning,
  GitCommitHorizontal,
  ScrollText,
  Waypoints,
} from 'lucide-react'

import { ConclusionBadge, ExecutionStatusBadge, SeverityBadge } from '@/components/dashboard/StatusBadge'
import { SourceStatusStrip } from '@/components/dashboard/SourceStatusStrip'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { EmptyState, StaleNotice } from '@/components/ui/empty-state'
import { Skeleton } from '@/components/ui/skeleton'
import type { DiscoveredApplication, IncidentEvent } from '@/lib/api'
import { useIncidentTimeline } from '@/lib/api'
import { formatDateTime, humanize } from '@/lib/format'

function EventIcon({ event }: { event: IncidentEvent }) {
  const Icon = event.source === 'deployments'
    ? GitCommitHorizontal
    : event.source === 'traces'
      ? Waypoints
      : event.source === 'logs'
        ? ScrollText
        : event.source === 'kubernetes' || event.source === 'events'
          ? Boxes
          : FileWarning
  return <Icon className="size-4" aria-hidden="true" />
}

export function IncidentTimelinePanel({ app, minutes }: { app?: DiscoveredApplication; minutes: number }) {
  const query = useIncidentTimeline(app, minutes)
  const envelope = query.data
  const timeline = envelope?.data

  return (
    <Card aria-busy={query.isLoading}>
      <CardHeader>
        <CardTitle>
          <Clock3 className="text-chart-1" aria-hidden="true" />
          Timeline correlacionada
          {envelope && <ExecutionStatusBadge status={envelope.executionStatus} />}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {!app && <EmptyState icon={CircleHelp} title="Selecione um serviço para reconstruir o incidente" />}
        {app && (!app.enabled || !app.namespace) && (
          <EmptyState
            icon={CircleHelp}
            title="Timeline indisponível"
            description="O serviço precisa estar habilitado e associado a um namespace."
          />
        )}
        {query.isLoading && !timeline && <Skeleton className="h-96 w-full" />}
        {query.isError && !timeline && (
          <EmptyState
            variant="error"
            icon={AlertCircle}
            title="Não foi possível correlacionar o incidente"
            description="A timeline precisa de pelo menos uma fonte temporal utilizável."
            action={{ label: 'Tentar novamente', onClick: () => query.refetch() }}
          />
        )}
        {query.isError && timeline && <StaleNotice onRetry={() => query.refetch()} />}

        {timeline && (
          <>
            <div className="flex flex-col gap-2 rounded-lg border bg-muted/20 px-3 py-2 sm:flex-row sm:items-center sm:justify-between">
              <div>
                <p className="text-xs text-muted-foreground">Início provável do incidente</p>
                <p className="font-mono text-xs tabular">{formatDateTime(timeline.incidentStartedAt)}</p>
              </div>
              <ConclusionBadge conclusion={timeline.analysisConclusion} />
            </div>
            <SourceStatusStrip sources={envelope.sources} />

            {timeline.events.length === 0 ? (
              <EmptyState
                icon={Clock3}
                title="Nenhum evento relevante na janela"
                description="As fontes consultadas não produziram deploys, anomalias, restarts, traces ou padrões de log correlacionáveis."
                className="py-7"
              />
            ) : (
              <ol className="relative ml-4 border-l border-border pl-6">
                {timeline.events.map((event) => (
                  <li key={event.id} className="relative pb-5 last:pb-0">
                    <span className="absolute -left-[2.38rem] top-0 flex size-7 items-center justify-center rounded-full border bg-background text-muted-foreground">
                      <EventIcon event={event} />
                    </span>
                    <article className="rounded-lg border border-border/70 p-3">
                      <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
                        <div>
                          <p className="text-sm font-medium">{humanize(event.type)}</p>
                          <p className="mt-1 text-xs leading-relaxed text-muted-foreground">{event.summary}</p>
                        </div>
                        <SeverityBadge severity={event.severity} />
                      </div>
                      <div className="mt-2 flex flex-wrap items-center gap-1.5">
                        <Badge variant="outline">{humanize(event.source)}</Badge>
                        <span className="font-mono text-[11px] text-muted-foreground tabular">
                          {formatDateTime(event.timestamp)}
                        </span>
                        <span className="text-[11px] text-muted-foreground">
                          {event.evidenceIds.length} evidência(s)
                        </span>
                      </div>
                    </article>
                  </li>
                ))}
              </ol>
            )}

            {timeline.correlations.length > 0 && (
              <details className="rounded-lg border px-3 py-2">
                <summary className="cursor-pointer text-xs font-medium">Correlações determinísticas ({timeline.correlations.length})</summary>
                <ul className="mt-2 space-y-1 font-mono text-[11px] text-muted-foreground">
                  {timeline.correlations.map((correlation) => <li key={correlation}>{correlation}</li>)}
                </ul>
              </details>
            )}
          </>
        )}
      </CardContent>
    </Card>
  )
}
