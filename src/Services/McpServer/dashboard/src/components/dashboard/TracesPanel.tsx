import { AlertCircle, Clock, ExternalLink, GitCommitHorizontal, Waypoints } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Card, CardAction, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
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
  const { data: traces, isLoading, isError } = useTraces(service, 12)
  const jaegerBase = overview?.links?.jaeger ?? overview?.links?.Jaeger

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <Waypoints className="size-4 text-chart-1" />
          Traces recentes
          {service && <span className="font-mono text-xs text-muted-foreground">· {service}</span>}
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
      <CardContent>
        {!service && <p className="py-6 text-center text-sm text-muted-foreground">Selecione um serviço acima.</p>}

        {service && isLoading && (
          <div className="space-y-2">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-10 w-full" />
            ))}
          </div>
        )}

        {service && isError && (
          <div className="flex items-center gap-2 rounded-lg border border-border bg-muted/40 p-3 text-sm text-muted-foreground">
            <AlertCircle className="size-4 shrink-0" />
            Sem dados de trace disponíveis (Jaeger pode não ter recebido spans ainda).
          </div>
        )}

        {service && !isLoading && !isError && (!traces || traces.length === 0) && (
          <p className="py-6 text-center text-sm text-muted-foreground">Nenhum trace encontrado na última hora.</p>
        )}

        {service && traces && traces.length > 0 && (
          <ul className="max-h-80 space-y-1.5 overflow-y-auto scrollbar-thin pr-1">
            {traces.map((trace) => (
              <li
                key={trace.traceId}
                className="flex items-center justify-between gap-3 rounded-lg border border-border/60 px-3 py-2 text-sm"
              >
                <div className="min-w-0 flex-1">
                  <p className="truncate font-medium">{trace.rootOperation}</p>
                  <p className="flex items-center gap-1 truncate font-mono text-xs text-muted-foreground">
                    <GitCommitHorizontal className="size-3" />
                    {trace.traceId.slice(0, 12)}
                  </p>
                </div>
                <div className="flex shrink-0 items-center gap-2">
                  <Badge variant="outline">{trace.spanCount} spans</Badge>
                  <Badge variant="outline">
                    <Clock className="size-3" />
                    {formatDuration(trace.durationMs)}
                  </Badge>
                </div>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}
