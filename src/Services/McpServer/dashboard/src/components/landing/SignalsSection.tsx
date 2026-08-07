import { useState, type ComponentType } from 'react'
import { Activity, ArrowRight, Boxes, FileJson, Flame, ScrollText, Waypoints } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Reveal } from '@/components/ui/reveal'
import { SectionHeading } from '@/components/landing/SectionHeading'
import { cn } from '@/lib/utils'

interface Signal {
  id: string
  icon: ComponentType<{ className?: string }>
  source: string
  title: string
  captures: string
  example: string
  normalized: string
  conclusion: string
  evidence: string[]
  tone: string
  surface: string
}
const SIGNALS: Signal[] = [
  {
    id: 'traces',
    icon: Waypoints,
    source: 'Jaeger',
    title: 'Traces',
    captures: 'Cadeia de chamadas, duração e erro de cada etapa.',
    example: 'ProdutoAPI → PrecoAPI · 2,4 s · HTTP 504',
    normalized: 'Dependência observada com latência e taxa de falha.',
    conclusion: 'A lentidão começa na chamada da ProdutoAPI para a PrecoAPI.',
    evidence: ['span da requisição', 'serviço de origem', 'serviço de destino'],
    tone: 'text-chart-2',
    surface: 'bg-chart-2/10',
  },
  {
    id: 'metrics',
    icon: Flame,
    source: 'Prometheus',
    title: 'Métricas',
    captures: 'Taxa de erros, latência, disponibilidade e saturação.',
    example: 'P95 +38% · erros 5xx 18% · janela 30 min',
    normalized: 'Score por dimensão com baseline e cobertura.',
    conclusion: 'A degradação é real, recente e afeta latência e erros.',
    evidence: ['P95', 'error rate', 'amostras da janela'],
    tone: 'text-chart-3',
    surface: 'bg-chart-3/10',
  },
  {
    id: 'kubernetes',
    icon: Boxes,
    source: 'Kubernetes',
    title: 'Cluster',
    captures: 'Pods, réplicas, restarts, revisions e Events.',
    example: 'revision 17 · 1/2 pods prontos · 4 restarts',
    normalized: 'Estado do workload ligado à mesma identidade de serviço.',
    conclusion: 'A regressão apareceu quatro minutos após o deploy da revisão 17.',
    evidence: ['Deployment', 'Pod status', 'Kubernetes Event'],
    tone: 'text-chart-1',
    surface: 'bg-chart-1/10',
  },
  {
    id: 'openapi',
    icon: FileJson,
    source: 'OpenAPI',
    title: 'Contratos',
    captures: 'Operações, rotas, métodos e schemas publicados.',
    example: 'GET /api/products · resposta 200 · Product[]',
    normalized: 'Catálogo de capacidades validado por aplicação.',
    conclusion: 'O agente sabe o que a API faz sem inventar endpoints.',
    evidence: ['operationId', 'response codes', 'schema'],
    tone: 'text-chart-4',
    surface: 'bg-chart-4/10',
  },
  {
    id: 'logs',
    icon: ScrollText,
    source: 'Loki',
    title: 'Logs',
    captures: 'Padrões de erro sanitizados e ligados ao intervalo do incidente.',
    example: '12 ocorrências · timeout upstream · sem PII',
    normalized: 'Fingerprint, severidade e frequência, sem consulta raw.',
    conclusion: 'Os timeouts confirmam a falha downstream vista nos traces.',
    evidence: ['fingerprint', 'timestamp', 'contagem'],
    tone: 'text-chart-2',
    surface: 'bg-chart-2/10',
  },
]

export function SignalsSection() {
  const [selectedId, setSelectedId] = useState(SIGNALS[0].id)
  const selected = SIGNALS.find((signal) => signal.id === selectedId) ?? SIGNALS[0]
  const SelectedIcon = selected.icon

  return (
    <section aria-labelledby="sinais-heading">
      <SectionHeading
        id="sinais-heading"
        eyebrow="Sinais"
        title="Veja o que cada fonte acrescenta à investigação"
        description="Selecione um sinal para acompanhar o caminho do dado bruto até uma conclusão verificável. Nenhuma cor ou animação é necessária para entender o resultado."
      />

      <div className="mt-6 grid gap-2 sm:grid-cols-2 lg:grid-cols-5" role="group" aria-label="Fontes de sinais observáveis">
        {SIGNALS.map((signal) => {
          const Icon = signal.icon
          const active = signal.id === selected.id
          return (
            <button
              key={signal.id}
              type="button"
              aria-pressed={active}
              aria-controls="signal-detail"
              onClick={() => setSelectedId(signal.id)}
              className={cn(
                'min-h-24 cursor-pointer rounded-xl border p-4 text-left transition-colors focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none',
                active ? 'border-primary bg-primary/5' : 'border-border bg-card hover:bg-accent/60',
              )}
            >
              <span className="flex items-center justify-between gap-2">
                <span className={cn('flex size-9 items-center justify-center rounded-lg', signal.surface)}>
                  <Icon className={cn('size-4', signal.tone)} aria-hidden="true" />
                </span>
                <span className={cn('size-2 rounded-full', active ? 'bg-primary' : 'bg-border')} aria-hidden="true" />
              </span>
              <span className="mt-3 block text-sm font-semibold">{signal.title}</span>
              <span className="block font-mono text-[11px] text-muted-foreground">{signal.source}</span>
            </button>
          )
        })}
      </div>

      <Reveal delay={60} className="mt-3">
        <Card id="signal-detail" className="overflow-hidden border-primary/30" aria-live="polite">
          <CardContent className="p-0">
            <div className="grid lg:grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)_auto_minmax(0,1fr)] lg:items-stretch">
              <div className="p-5">
                <div className="flex items-center gap-2">
                  <span className={cn('flex size-9 items-center justify-center rounded-lg', selected.surface)}>
                    <SelectedIcon className={cn('size-4', selected.tone)} aria-hidden="true" />
                  </span>
                  <div>
                    <p className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">1 · O que chega</p>
                    <p className="text-sm font-semibold">{selected.source}</p>
                  </div>
                </div>
                <p className="mt-3 text-sm leading-relaxed text-muted-foreground">{selected.captures}</p>
                <code className="mt-3 block rounded-lg bg-muted px-3 py-2 font-mono text-xs leading-relaxed">{selected.example}</code>
              </div>

              <div className="hidden items-center text-muted-foreground lg:flex" aria-hidden="true">
                <ArrowRight className="size-4" />
              </div>

              <div className="border-t border-border p-5 lg:border-t-0">
                <div className="flex items-center gap-2">
                  <span className="flex size-9 items-center justify-center rounded-lg bg-primary/10 text-primary">
                    <Activity className="size-4" aria-hidden="true" />
                  </span>
                  <div>
                    <p className="text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">2 · Como é tratado</p>
                    <p className="text-sm font-semibold">Provider normalizado</p>
                  </div>
                </div>
                <p className="mt-3 text-sm leading-relaxed text-muted-foreground">{selected.normalized}</p>
                <div className="mt-3 flex flex-wrap gap-1.5">
                  {selected.evidence.map((item) => <Badge key={item} variant="outline">{item}</Badge>)}
                </div>
              </div>

              <div className="hidden items-center text-muted-foreground lg:flex" aria-hidden="true">
                <ArrowRight className="size-4" />
              </div>

              <div className="border-t border-border bg-primary/5 p-5 lg:border-t-0">
                <p className="text-[11px] font-semibold uppercase tracking-wider text-primary">3 · O que o agente recebe</p>
                <p className="mt-2 text-base font-semibold leading-snug">{selected.conclusion}</p>
                <p className="mt-3 text-xs leading-relaxed text-muted-foreground">
                  Com evidências, freshness, cobertura e limitações explícitas — sem expor consultas raw.
                </p>
              </div>
            </div>
          </CardContent>
        </Card>
      </Reveal>
    </section>
  )
}
