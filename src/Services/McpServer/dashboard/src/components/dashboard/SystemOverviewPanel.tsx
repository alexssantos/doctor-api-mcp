import {
  Activity,
  AlertCircle,
  AlertTriangle,
  ArrowRight,
  CheckCircle2,
  CircleHelp,
  ServerCog,
} from 'lucide-react'
import type { ComponentType } from 'react'

import { HealthStatusBadge } from '@/components/dashboard/StatusBadge'
import { SourceStatusStrip } from '@/components/dashboard/SourceStatusStrip'
import { Button } from '@/components/ui/button'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { EmptyState } from '@/components/ui/empty-state'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import type { OverviewResponse, ServiceIdentity } from '@/lib/api'
import { formatPercent } from '@/lib/format'

function MetricTile({
  label,
  value,
  detail,
  icon: Icon,
  tone,
}: {
  label: string
  value: string
  detail: string
  icon: ComponentType<{ className?: string }>
  tone: string
}) {
  return (
    <Card>
      <CardContent className="flex min-h-28 items-start justify-between gap-3 p-4">
        <div>
          <p className="text-xs font-medium text-muted-foreground">{label}</p>
          <p className="mt-1 text-2xl font-semibold tracking-tight tabular">{value}</p>
          <p className="mt-1 text-xs text-muted-foreground">{detail}</p>
        </div>
        <span className={`flex size-10 shrink-0 items-center justify-center rounded-lg ${tone}`}>
          <Icon className="size-5" aria-hidden="true" />
        </span>
      </CardContent>
    </Card>
  )
}

export function SystemOverviewPanel({
  overview,
  isLoading,
  isError,
  onRetry,
  onInspect,
}: {
  overview?: OverviewResponse
  isLoading: boolean
  isError: boolean
  onRetry: () => void
  onInspect: (service: ServiceIdentity) => void
}) {
  if (isLoading && !overview) {
    return (
      <div className="space-y-4" aria-busy="true">
        <div className="grid grid-cols-2 gap-3 lg:grid-cols-5">
          {Array.from({ length: 5 }).map((_, index) => (
            <Skeleton key={index} className="h-28 w-full" />
          ))}
        </div>
        <Skeleton className="h-80 w-full" />
      </div>
    )
  }

  if (isError && !overview) {
    return (
      <EmptyState
        variant="error"
        icon={AlertCircle}
        title="Não foi possível consolidar a saúde do sistema"
        description="O backend não retornou o resumo dos engines. Verifique autenticação e conectividade com Kubernetes e Prometheus."
        action={{ label: 'Tentar novamente', onClick: onRetry }}
      />
    )
  }

  if (!overview) return null
  const system = overview.system

  return (
    <div className="space-y-4">
      <section aria-labelledby="system-summary-heading" className="space-y-3">
        <div className="flex flex-col gap-2 sm:flex-row sm:items-end sm:justify-between">
          <div>
            <p className="text-xs font-medium uppercase tracking-[0.16em] text-muted-foreground">
              Visão SRE · janela {overview.window.duration}
            </p>
            <div className="mt-1 flex flex-wrap items-center gap-2">
              <h2 id="system-summary-heading" className="text-xl font-semibold tracking-tight">
                Saúde consolidada
              </h2>
              <HealthStatusBadge status={system.healthStatus} />
            </div>
          </div>
          <SourceStatusStrip sources={overview.sources} />
        </div>

        <div className="grid grid-cols-2 gap-3 lg:grid-cols-5">
          <MetricTile
            label="Serviços analisados"
            value={String(system.totalServices)}
            detail={`${overview.cluster.readyPods}/${overview.cluster.totalPods} pods prontos`}
            icon={ServerCog}
            tone="bg-primary/12 text-primary"
          />
          <MetricTile
            label="Saudáveis"
            value={String(system.healthy)}
            detail="score e cobertura suficientes"
            icon={CheckCircle2}
            tone="bg-success/15 text-success-fg"
          />
          <MetricTile
            label="Degradados"
            value={String(system.degraded)}
            detail="atenção operacional"
            icon={AlertTriangle}
            tone="bg-warning/15 text-warning-fg"
          />
          <MetricTile
            label="Críticos"
            value={String(system.critical)}
            detail="ação investigativa prioritária"
            icon={Activity}
            tone="bg-destructive/12 text-destructive"
          />
          <MetricTile
            label="Desconhecidos"
            value={String(system.unknown)}
            detail="cobertura insuficiente"
            icon={CircleHelp}
            tone="bg-muted text-muted-foreground"
          />
        </div>
      </section>

      {overview.warnings.length > 0 && (
        <div role="status" className="rounded-lg border border-warning/30 bg-warning/10 px-3 py-2 text-xs text-warning-fg">
          {overview.warnings.join(' · ')}
        </div>
      )}

      <Card>
        <CardHeader>
          <CardTitle>Prioridade por serviço</CardTitle>
        </CardHeader>
        <CardContent className="px-0 pb-0">
          {system.services.length === 0 ? (
            <EmptyState
              icon={CircleHelp}
              title="Nenhum serviço habilitado para análise"
              description="Habilite aplicações no catálogo para incluí-las no resumo sistêmico."
              className="m-5"
            />
          ) : (
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Serviço</TableHead>
                  <TableHead>Estado</TableHead>
                  <TableHead className="text-right">Score</TableHead>
                  <TableHead className="text-right">Cobertura</TableHead>
                  <TableHead className="text-right">Findings críticos</TableHead>
                  <TableHead><span className="sr-only">Ação</span></TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                {system.services.map((summary) => (
                  <TableRow key={`${summary.service.namespace}/${summary.service.serviceName}`}>
                    <TableCell>
                      <p className="font-mono text-xs font-medium">{summary.service.serviceName}</p>
                      <p className="text-xs text-muted-foreground">{summary.service.namespace}</p>
                    </TableCell>
                    <TableCell><HealthStatusBadge status={summary.healthStatus} /></TableCell>
                    <TableCell className="text-right font-mono tabular">
                      {summary.score == null ? '—' : summary.score.toFixed(0)}
                    </TableCell>
                    <TableCell className="text-right font-mono tabular">
                      {formatPercent(summary.coverage)}
                    </TableCell>
                    <TableCell className="text-right font-mono tabular">{summary.criticalFindings}</TableCell>
                    <TableCell className="text-right">
                      <Button variant="ghost" size="sm" onClick={() => onInspect(summary.service)}>
                        Analisar <ArrowRight aria-hidden="true" />
                      </Button>
                    </TableCell>
                  </TableRow>
                ))}
              </TableBody>
            </Table>
          )}
        </CardContent>
      </Card>
    </div>
  )
}
