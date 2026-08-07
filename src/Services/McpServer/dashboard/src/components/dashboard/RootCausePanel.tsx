import { AlertCircle, BrainCircuit, CircleHelp, Lightbulb, ShieldAlert, ShieldCheck } from 'lucide-react'

import { ConclusionBadge, ExecutionStatusBadge } from '@/components/dashboard/StatusBadge'
import { SourceStatusStrip } from '@/components/dashboard/SourceStatusStrip'
import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { EmptyState, StaleNotice } from '@/components/ui/empty-state'
import { Progress } from '@/components/ui/progress'
import { Skeleton } from '@/components/ui/skeleton'
import type { DiscoveredApplication, RootCauseHypothesis } from '@/lib/api'
import { useRootCause } from '@/lib/api'
import { formatPercent, humanize } from '@/lib/format'

function HypothesisCard({ hypothesis, primary = false }: { hypothesis: RootCauseHypothesis; primary?: boolean }) {
  return (
    <div className={primary ? 'rounded-lg border border-primary/40 bg-primary/5 p-4' : 'rounded-lg border p-3'}>
      <div className="flex flex-col gap-2 sm:flex-row sm:items-start sm:justify-between">
        <div>
          <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
            {primary ? 'Hipótese principal' : humanize(hypothesis.id)}
          </p>
          <p className="mt-1 text-sm font-medium leading-relaxed">{hypothesis.summary}</p>
        </div>
        <Badge variant={hypothesis.confidence >= 0.75 ? 'success' : hypothesis.confidence >= 0.5 ? 'warning' : 'outline'}>
          confiança {formatPercent(hypothesis.confidence)}
        </Badge>
      </div>
      <Progress value={hypothesis.confidence * 100} className="mt-3" aria-label="Confiança da hipótese" />
      <div className="mt-3 flex flex-wrap gap-2 text-[11px] text-muted-foreground">
        <span>{hypothesis.supportingEvidenceIds.length} evidência(s) favorável(is)</span>
        <span>·</span>
        <span>{hypothesis.contradictingEvidenceIds.length} contrária(s)</span>
        <span>·</span>
        <span>{hypothesis.potentiallyAffectedServices.length} serviço(s) no impacto</span>
      </div>
    </div>
  )
}

export function RootCausePanel({ app, minutes }: { app?: DiscoveredApplication; minutes: number }) {
  const query = useRootCause(app, minutes, 2)
  const envelope = query.data
  const report = envelope?.data

  return (
    <Card aria-busy={query.isLoading}>
      <CardHeader>
        <CardTitle>
          <BrainCircuit className="text-chart-4" aria-hidden="true" />
          RCA explicável
          {envelope && <ExecutionStatusBadge status={envelope.executionStatus} />}
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        {!app && <EmptyState icon={CircleHelp} title="Selecione um serviço para gerar hipóteses" />}
        {app && (!app.enabled || !app.namespace) && (
          <EmptyState
            icon={CircleHelp}
            title="RCA indisponível"
            description="O serviço precisa estar habilitado e associado a um namespace."
          />
        )}
        {query.isLoading && !report && <Skeleton className="h-96 w-full" />}
        {query.isError && !report && (
          <EmptyState
            variant="error"
            icon={AlertCircle}
            title="Não foi possível executar o RCA"
            description="Health, dependências e timeline não puderam ser consolidados."
            action={{ label: 'Tentar novamente', onClick: () => query.refetch() }}
          />
        )}
        {query.isError && report && <StaleNotice onRetry={() => query.refetch()} />}

        {report && (
          <>
            <div className="flex flex-wrap items-center justify-between gap-2 rounded-lg border bg-muted/20 px-3 py-2">
              <div>
                <p className="text-xs text-muted-foreground">Cobertura causal</p>
                <p className="font-mono text-sm font-semibold tabular">{formatPercent(report.coverage)}</p>
              </div>
              <ConclusionBadge conclusion={report.analysisConclusion} />
            </div>
            <SourceStatusStrip sources={envelope.sources} />

            {report.primaryHypothesis ? (
              <HypothesisCard hypothesis={report.primaryHypothesis} primary />
            ) : (
              <EmptyState
                icon={ShieldAlert}
                title={report.analysisConclusion === 'notDetected' ? 'Nenhum incidente detectado' : 'Evidência insuficiente para causa raiz'}
                description="O engine não promove hipóteses abaixo do limiar determinístico de confiança."
                className="py-6"
              />
            )}

            {report.alternatives.length > 0 && (
              <details className="rounded-lg border px-3 py-2">
                <summary className="cursor-pointer text-xs font-medium">Hipóteses alternativas ({report.alternatives.length})</summary>
                <div className="mt-3 space-y-2">
                  {report.alternatives.map((hypothesis) => <HypothesisCard key={hypothesis.id} hypothesis={hypothesis} />)}
                </div>
              </details>
            )}

            {report.limitations.length > 0 && (
              <div role="status" className="rounded-lg border border-warning/30 bg-warning/8 p-3">
                <p className="flex items-center gap-1.5 text-xs font-medium text-warning-fg">
                  <ShieldAlert className="size-3.5" aria-hidden="true" /> Limitações
                </p>
                <ul className="mt-2 list-disc space-y-1 pl-4 text-xs text-warning-fg">
                  {report.limitations.map((limitation) => <li key={limitation}>{limitation}</li>)}
                </ul>
              </div>
            )}

            <section aria-labelledby="recommendations-title">
              <h3 id="recommendations-title" className="mb-2 flex items-center gap-1.5 text-sm font-semibold">
                <Lightbulb className="size-4 text-warning-fg" aria-hidden="true" /> Próximos passos
              </h3>
              {report.recommendations.length === 0 ? (
                <p className="rounded-lg border border-dashed px-3 py-4 text-center text-xs text-muted-foreground">
                  Nenhuma recomendação necessária nesta janela.
                </p>
              ) : (
                <ol className="space-y-2">
                  {report.recommendations.map((recommendation, index) => (
                    <li key={`${recommendation.priority}-${index}`} className="rounded-lg border px-3 py-3">
                      <div className="flex items-start gap-3">
                        <Badge variant={recommendation.priority === 'P1' ? 'warning' : 'outline'}>{recommendation.priority}</Badge>
                        <div className="min-w-0 flex-1">
                          <p className="text-sm font-medium leading-relaxed">{recommendation.action}</p>
                          <p className="mt-1 text-xs leading-relaxed text-muted-foreground">{recommendation.reason}</p>
                        </div>
                        <Badge variant="secondary" title="O MCP não executa mudanças">
                          <ShieldCheck aria-hidden="true" /> somente leitura
                        </Badge>
                      </div>
                    </li>
                  ))}
                </ol>
              )}
            </section>
          </>
        )}
      </CardContent>
    </Card>
  )
}
