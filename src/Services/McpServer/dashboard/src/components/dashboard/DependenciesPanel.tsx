import { AlertCircle, ArrowRight, Network } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { EmptyState, StaleNotice } from '@/components/ui/empty-state'
import { Skeleton } from '@/components/ui/skeleton'
import { useDependencies } from '@/lib/api'

export function DependenciesPanel() {
  const { data: dependencies, isLoading, isError, refetch } = useDependencies()

  // Prefer showing the last known graph over an error that contradicts it.
  const hasData = Boolean(dependencies && dependencies.length > 0)
  const showError = isError && !hasData

  return (
    <Card aria-busy={isLoading}>
      <CardHeader>
        <CardTitle>
          <Network className="size-4 text-chart-2" />
          Dependências entre serviços
          {dependencies && dependencies.length > 0 && (
            <span className="text-xs font-normal text-muted-foreground tabular">
              {dependencies.length} aresta{dependencies.length > 1 ? 's' : ''}
            </span>
          )}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-2">
        {isLoading && (
          <div className="space-y-2">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-9 w-full" />
            ))}
          </div>
        )}

        {showError && (
          <EmptyState
            variant="error"
            icon={AlertCircle}
            title="Sem dados de dependências"
            description="O Jaeger ainda não expôs o grafo de dependências. Ele é recalculado periodicamente."
            action={{ label: 'Tentar novamente', onClick: () => refetch() }}
          />
        )}

        {isError && hasData && <StaleNotice onRetry={() => refetch()} />}

        {!isLoading && !isError && !hasData && (
          <EmptyState
            icon={Network}
            title="Nenhuma dependência detectada"
            description="Gere tráfego entre os serviços e aguarde o Jaeger processar os spans para montar o grafo."
            action={{ label: 'Atualizar', onClick: () => refetch() }}
          />
        )}

        {hasData && (
          <ul className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            {dependencies!.map((edge, i) => (
              <li
                key={`${edge.parent}-${edge.child}-${i}`}
                className="flex items-center justify-between gap-2 rounded-lg border border-border/60 px-3 py-2 text-sm transition-colors hover:border-border hover:bg-accent/30"
              >
                <div className="flex min-w-0 items-center gap-1.5 font-mono text-xs">
                  <span className="truncate">{edge.parent ?? '?'}</span>
                  <ArrowRight className="size-3.5 shrink-0 text-muted-foreground" />
                  <span className="truncate">{edge.child ?? '?'}</span>
                </div>
                <Badge variant="outline" className="tabular">
                  {edge.callCount ?? 0} chamadas
                </Badge>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}
