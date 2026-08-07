import { AlertCircle, ArrowDownLeft, ArrowRight, ArrowUpRight, CircleHelp, Network } from 'lucide-react'

import { ExecutionStatusBadge } from '@/components/dashboard/StatusBadge'
import { SourceStatusStrip } from '@/components/dashboard/SourceStatusStrip'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { EmptyState, StaleNotice } from '@/components/ui/empty-state'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import type { DependencyEdge, DiscoveredApplication } from '@/lib/api'
import { useServiceDependencies } from '@/lib/api'
import { formatNumber, formatPercent } from '@/lib/format'

function EdgeTable({ title, direction, edges }: { title: string; direction: 'in' | 'out'; edges: DependencyEdge[] }) {
  const Icon = direction === 'in' ? ArrowDownLeft : ArrowUpRight
  return (
    <section aria-label={title} className="min-w-0">
      <h3 className="mb-2 flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-muted-foreground">
        <Icon className="size-3.5" aria-hidden="true" /> {title} · {edges.length}
      </h3>
      {edges.length === 0 ? (
        <p className="rounded-lg border border-dashed px-3 py-4 text-center text-xs text-muted-foreground">
          Nenhuma aresta resolvida.
        </p>
      ) : (
        <div className="min-w-0 rounded-lg border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Origem</TableHead>
                <TableHead>Destino</TableHead>
                <TableHead className="text-right">Chamadas</TableHead>
                <TableHead className="text-right">Erros</TableHead>
                <TableHead className="text-right">Latência</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {edges.map((edge, index) => (
                <TableRow key={`${edge.source.namespace}/${edge.source.serviceName}-${edge.target.namespace}/${edge.target.serviceName}-${index}`}>
                  <TableCell className="font-mono text-xs">{edge.source.serviceName}</TableCell>
                  <TableCell className="font-mono text-xs">{edge.target.serviceName}</TableCell>
                  <TableCell className="text-right font-mono text-xs tabular">{edge.callCount}</TableCell>
                  <TableCell className="text-right font-mono text-xs tabular">{formatPercent(edge.errorRate)}</TableCell>
                  <TableCell className="text-right font-mono text-xs tabular">
                    {formatNumber(edge.latencyMilliseconds, 'ms')}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}
    </section>
  )
}

export function DependenciesPanel({ app, minutes }: { app?: DiscoveredApplication; minutes: number }) {
  const query = useServiceDependencies(app, minutes, 2)
  const envelope = query.data
  const graph = envelope?.data

  return (
    <Card aria-busy={query.isLoading}>
      <CardHeader>
        <CardTitle>
          <Network className="text-chart-2" aria-hidden="true" />
          Dependency Engine
          {envelope && <ExecutionStatusBadge status={envelope.executionStatus} />}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {!app && <EmptyState icon={CircleHelp} title="Selecione um serviço para montar o grafo" />}
        {app && (!app.enabled || !app.namespace) && (
          <EmptyState
            icon={CircleHelp}
            title="Grafo indisponível"
            description="O serviço precisa estar habilitado e associado a um namespace."
          />
        )}
        {query.isLoading && !graph && <Skeleton className="h-72 w-full" />}
        {query.isError && !graph && (
          <EmptyState
            variant="error"
            icon={AlertCircle}
            title="Não foi possível montar o grafo causal"
            description="Os traces ou aliases de serviço não puderam ser resolvidos."
            action={{ label: 'Tentar novamente', onClick: () => query.refetch() }}
          />
        )}
        {query.isError && graph && <StaleNotice onRetry={() => query.refetch()} />}

        {graph && (
          <>
            <div className="grid gap-3 sm:grid-cols-3">
              <div className="rounded-lg border p-3">
                <p className="text-xs text-muted-foreground">Nós resolvidos</p>
                <p className="mt-1 font-mono text-xl font-semibold tabular">{graph.nodes.length}</p>
              </div>
              <div className="rounded-lg border p-3">
                <p className="text-xs text-muted-foreground">Blast radius potencial</p>
                <p className="mt-1 font-mono text-xl font-semibold tabular">{graph.potentialBlastRadius.length}</p>
              </div>
              <div className="rounded-lg border p-3">
                <p className="text-xs text-muted-foreground">Ciclos detectados</p>
                <p className="mt-1 font-mono text-xl font-semibold tabular">{graph.cycles.length}</p>
              </div>
            </div>

            {graph.criticalPath.length > 1 && (
              <div className="rounded-lg border bg-muted/20 p-3">
                <p className="mb-2 text-xs font-medium text-muted-foreground">Caminho crítico observado</p>
                <div className="flex flex-wrap items-center gap-1.5">
                  {graph.criticalPath.map((node, index) => (
                    <span key={`${node}-${index}`} className="contents">
                      <Badge variant="outline" className="font-mono">{node}</Badge>
                      {index < graph.criticalPath.length - 1 && <ArrowRight className="size-3.5 text-muted-foreground" />}
                    </span>
                  ))}
                </div>
              </div>
            )}

            <SourceStatusStrip sources={envelope.sources} />

            <div className="grid min-w-0 grid-cols-1 gap-4 xl:grid-cols-2">
              <EdgeTable title="Dependências de entrada" direction="in" edges={graph.inbound} />
              <EdgeTable title="Dependências de saída" direction="out" edges={graph.outbound} />
            </div>

            {(graph.cycles.length > 0 || graph.potentialBlastRadius.length > 0) && (
              <div className="grid gap-3 sm:grid-cols-2">
                <div className="rounded-lg border p-3">
                  <p className="text-xs font-medium">Ciclos</p>
                  <ul className="mt-2 space-y-1 font-mono text-[11px] text-muted-foreground">
                    {graph.cycles.map((cycle, index) => <li key={index}>{cycle.join(' → ')}</li>)}
                    {graph.cycles.length === 0 && <li>nenhum</li>}
                  </ul>
                </div>
                <div className="rounded-lg border p-3">
                  <p className="text-xs font-medium">Serviços potencialmente afetados</p>
                  <div className="mt-2 flex flex-wrap gap-1.5">
                    {graph.potentialBlastRadius.map((service) => (
                      <Badge key={service} variant="secondary" className="font-mono">{service}</Badge>
                    ))}
                    {graph.potentialBlastRadius.length === 0 && <span className="text-xs text-muted-foreground">nenhum</span>}
                  </div>
                </div>
              </div>
            )}
          </>
        )}
      </CardContent>
    </Card>
  )
}
