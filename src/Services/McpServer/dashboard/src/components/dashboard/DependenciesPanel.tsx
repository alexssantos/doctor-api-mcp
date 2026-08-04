import { AlertCircle, ArrowRight, Network } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { useDependencies } from '@/lib/api'

export function DependenciesPanel() {
  const { data: dependencies, isLoading, isError } = useDependencies()

  return (
    <Card>
      <CardHeader>
        <CardTitle>
          <Network className="size-4 text-chart-2" />
          Dependências entre serviços
        </CardTitle>
      </CardHeader>
      <CardContent>
        {isLoading && (
          <div className="space-y-2">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-9 w-full" />
            ))}
          </div>
        )}

        {isError && (
          <div className="flex items-center gap-2 rounded-lg border border-border bg-muted/40 p-3 text-sm text-muted-foreground">
            <AlertCircle className="size-4 shrink-0" />
            Sem dados de dependências disponíveis no Jaeger ainda.
          </div>
        )}

        {!isLoading && !isError && (!dependencies || dependencies.length === 0) && (
          <p className="py-6 text-center text-sm text-muted-foreground">
            Nenhuma dependência detectada. Gere tráfego entre os serviços e aguarde o Jaeger processar.
          </p>
        )}

        {dependencies && dependencies.length > 0 && (
          <ul className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            {dependencies.map((edge, i) => (
              <li
                key={`${edge.parent}-${edge.child}-${i}`}
                className="flex items-center justify-between gap-2 rounded-lg border border-border/60 px-3 py-2 text-sm"
              >
                <div className="flex min-w-0 items-center gap-1.5 font-mono text-xs">
                  <span className="truncate">{edge.parent ?? '?'}</span>
                  <ArrowRight className="size-3.5 shrink-0 text-muted-foreground" />
                  <span className="truncate">{edge.child ?? '?'}</span>
                </div>
                <Badge variant="outline">{edge.callCount ?? 0} chamadas</Badge>
              </li>
            ))}
          </ul>
        )}
      </CardContent>
    </Card>
  )
}
