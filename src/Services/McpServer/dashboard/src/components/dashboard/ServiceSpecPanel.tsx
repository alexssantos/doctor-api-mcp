import { AlertCircle, Boxes, CircleHelp, FileJson2, Package, Users } from 'lucide-react'

import { AvailabilityBadge, ExecutionStatusBadge } from '@/components/dashboard/StatusBadge'
import { SourceStatusStrip } from '@/components/dashboard/SourceStatusStrip'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { EmptyState, StaleNotice } from '@/components/ui/empty-state'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import type { DiscoveredApplication, SignalCoverage } from '@/lib/api'
import { useServiceSpec } from '@/lib/api'
import { humanize } from '@/lib/format'

const SIGNALS: Array<keyof SignalCoverage> = ['kubernetes', 'metrics', 'traces', 'logs', 'openApi', 'events']

export function ServiceSpecPanel({ app }: { app?: DiscoveredApplication }) {
  const query = useServiceSpec(app)
  const envelope = query.data
  const spec = envelope?.data

  return (
    <Card aria-busy={query.isLoading}>
      <CardHeader>
        <CardTitle>
          <FileJson2 className="text-chart-2" aria-hidden="true" />
          Catálogo técnico
          {envelope && <ExecutionStatusBadge status={envelope.executionStatus} />}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {!app && <EmptyState icon={CircleHelp} title="Selecione um serviço para ver sua especificação" />}
        {app && (!app.enabled || !app.namespace) && (
          <EmptyState
            icon={CircleHelp}
            title="Especificação não consultável"
            description="O serviço precisa estar habilitado e associado a um namespace."
          />
        )}
        {query.isLoading && !spec && <Skeleton className="h-72 w-full" />}
        {query.isError && !spec && (
          <EmptyState
            variant="error"
            icon={AlertCircle}
            title="Não foi possível consolidar a especificação"
            description="O catálogo ou a spec OpenAPI não respondeu. A ausência de OpenAPI deve aparecer como resposta parcial."
            action={{ label: 'Tentar novamente', onClick: () => query.refetch() }}
          />
        )}
        {query.isError && spec && <StaleNotice onRetry={() => query.refetch()} />}

        {spec && (
          <>
            <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
              <div className="rounded-lg border p-3">
                <p className="flex items-center gap-1.5 text-xs text-muted-foreground"><Package className="size-3.5" /> Versão / revisão</p>
                <p className="mt-1 truncate font-mono text-xs" title={spec.image ?? undefined}>
                  {spec.version ?? spec.image ?? 'não declarada'}
                </p>
                <p className="mt-0.5 text-[11px] text-muted-foreground">rev. {spec.revision ?? '—'}</p>
              </div>
              <div className="rounded-lg border p-3">
                <p className="flex items-center gap-1.5 text-xs text-muted-foreground"><Boxes className="size-3.5" /> Réplicas</p>
                <p className="mt-1 font-mono text-sm tabular">{spec.readyReplicas}/{spec.desiredReplicas} prontas</p>
              </div>
              <div className="rounded-lg border p-3">
                <p className="flex items-center gap-1.5 text-xs text-muted-foreground"><Users className="size-3.5" /> Ownership</p>
                <p className="mt-1 text-sm">{spec.team ?? spec.owner ?? 'não informado'}</p>
              </div>
              <div className="rounded-lg border p-3">
                <p className="text-xs text-muted-foreground">Dependências declaradas</p>
                <p className="mt-1 font-mono text-sm tabular">{spec.declaredDependencies.length}</p>
              </div>
            </div>

            <div>
              <h3 className="mb-2 text-sm font-semibold">Cobertura de sinais</h3>
              <div className="grid grid-cols-2 gap-2 sm:grid-cols-3">
                {SIGNALS.map((signal) => (
                  <div key={signal} className="flex items-center justify-between gap-2 rounded-lg border px-2.5 py-2">
                    <span className="text-xs">{humanize(signal)}</span>
                    <AvailabilityBadge availability={spec.coverage[signal]} />
                  </div>
                ))}
              </div>
            </div>

            <SourceStatusStrip sources={envelope.sources} />

            <section aria-labelledby="spec-endpoints-title">
              <div className="mb-2 flex items-center justify-between gap-2">
                <h3 id="spec-endpoints-title" className="text-sm font-semibold">Endpoints OpenAPI</h3>
                <Badge variant="outline" className="tabular">{spec.endpoints.length}</Badge>
              </div>
              {spec.endpoints.length === 0 ? (
                <EmptyState
                  icon={FileJson2}
                  title="OpenAPI indisponível"
                  description="O serviço continua visível, mas não há operações indexadas."
                  className="py-6"
                />
              ) : (
                <div className="max-h-72 overflow-y-auto rounded-lg border scrollbar-thin">
                  <Table>
                    <TableHeader>
                      <TableRow>
                        <TableHead>Método</TableHead>
                        <TableHead>Rota</TableHead>
                        <TableHead>Resumo</TableHead>
                      </TableRow>
                    </TableHeader>
                    <TableBody>
                      {spec.endpoints.map((endpoint) => (
                        <TableRow key={`${endpoint.method}-${endpoint.path}`}>
                          <TableCell><Badge variant="outline">{endpoint.method}</Badge></TableCell>
                          <TableCell className="max-w-64 truncate font-mono text-xs" title={endpoint.path}>{endpoint.path}</TableCell>
                          <TableCell className="text-xs text-muted-foreground">{endpoint.summary ?? endpoint.operationId ?? '—'}</TableCell>
                        </TableRow>
                      ))}
                    </TableBody>
                  </Table>
                </div>
              )}
            </section>
          </>
        )}
      </CardContent>
    </Card>
  )
}
