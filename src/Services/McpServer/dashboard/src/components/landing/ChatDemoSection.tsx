import { useState } from 'react'
import { ArrowRight, Bot, CheckCircle2, CircleHelp, ShieldCheck } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Reveal } from '@/components/ui/reveal'
import { SectionHeading } from '@/components/landing/SectionHeading'
import { MCP_TOOLS } from '@/lib/mcp-tools'
import { cn } from '@/lib/utils'

const SCENARIOS = [
  {
    id: 'health',
    label: 'Saúde agora',
    question: 'A ProdutoAPI está saudável?',
    tool: 'service_get_health',
    answer: 'Degradada, score 72. Os pods estão prontos, mas o P95 subiu 38% nos últimos 30 minutos.',
    evidence: ['Kubernetes 2/2 prontos', 'Prometheus P95 +38%', 'cobertura 92%'],
    limitation: 'Jaeger está stale; traces não influenciaram esta conclusão.',
  },
  {
    id: 'timeline',
    label: 'Depois do deploy',
    question: 'O que aconteceu depois do deploy da PrecoAPI?',
    tool: 'service_get_incident_timeline',
    answer: 'A revisão 17 foi seguida por latência anômala em 4 minutos e pelo aumento de erros 5xx em 6 minutos.',
    evidence: ['14:08 deploy rev.17', '14:12 P95 anômalo', '14:14 erros 5xx'],
    limitation: 'A ordem temporal é confirmada; causalidade ainda é uma hipótese.',
  },
  {
    id: 'root-cause',
    label: 'Causa provável',
    question: 'Qual é a causa raiz mais provável?',
    tool: 'service_find_root_cause',
    answer: 'Hipótese principal, confiança 86%: regressão introduzida pela revisão 17 da PrecoAPI.',
    evidence: ['3 evidências favoráveis', '0 evidências contrárias', 'blast radius: ProdutoAPI'],
    limitation: 'A recomendação é somente leitura: comparar a revisão 17 com a anterior.',
  },
] as const

export function ChatDemoSection() {
  const [selectedId, setSelectedId] = useState<(typeof SCENARIOS)[number]['id']>('health')
  const selected = SCENARIOS.find((scenario) => scenario.id === selectedId) ?? SCENARIOS[0]
  const tool = MCP_TOOLS.find((item) => item.name === selected.tool)
  const ToolIcon = tool?.icon

  return (
    <section aria-labelledby="chat-heading">
      <SectionHeading
        id="chat-heading"
        eyebrow="Na prática"
        title="Escolha uma pergunta e veja como a resposta é construída"
        description="Cada cenário deixa explícitos a tool usada, as evidências que sustentam a conclusão e aquilo que ainda não pode ser afirmado."
      />

      <div className="mt-6 grid gap-4 lg:grid-cols-[minmax(15rem,0.65fr)_minmax(0,1.35fr)]">
        <Reveal>
          <div className="grid gap-2" role="group" aria-label="Cenários de investigação">
            {SCENARIOS.map((scenario, index) => {
              const active = scenario.id === selected.id
              return (
                <button
                  key={scenario.id}
                  type="button"
                  aria-pressed={active}
                  aria-controls="scenario-detail"
                  onClick={() => setSelectedId(scenario.id)}
                  className={cn(
                    'flex min-h-16 cursor-pointer items-center gap-3 rounded-xl border p-3 text-left transition-colors focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none',
                    active ? 'border-primary bg-primary/5' : 'border-border bg-card hover:bg-accent/60',
                  )}
                >
                  <span className={cn(
                    'flex size-9 shrink-0 items-center justify-center rounded-lg text-sm font-semibold tabular',
                    active ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground',
                  )}>
                    {index + 1}
                  </span>
                  <span className="min-w-0">
                    <span className="block text-sm font-semibold">{scenario.label}</span>
                    <span className="mt-0.5 block text-xs leading-relaxed text-muted-foreground">{scenario.question}</span>
                  </span>
                  <ArrowRight className={cn('ml-auto size-4 shrink-0', active ? 'text-primary' : 'text-muted-foreground')} aria-hidden="true" />
                </button>
              )
            })}
          </div>
        </Reveal>

        <Reveal delay={60}>
          <Card id="scenario-detail" className="h-full overflow-hidden" aria-live="polite">
            <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border bg-muted/35 px-4 py-3">
              <div className="flex items-center gap-2 text-xs text-muted-foreground">
                <span className="flex size-7 items-center justify-center rounded-full bg-primary/10 text-primary">
                  <Bot className="size-3.5" aria-hidden="true" />
                </span>
                investigação orientada por evidências
              </div>
              <Badge variant="outline" className="gap-1 font-mono text-[10px]">
                {ToolIcon && <ToolIcon className="size-3" aria-hidden="true" />}
                {selected.tool}
              </Badge>
            </div>

            <CardContent className="space-y-4 p-4 sm:p-5">
              <div>
                <p className="flex items-center gap-2 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
                  <CircleHelp className="size-3.5" aria-hidden="true" />
                  Pergunta do agente
                </p>
                <p className="mt-2 rounded-xl bg-primary px-4 py-3 text-sm font-medium text-primary-foreground">{selected.question}</p>
              </div>

              <div>
                <p className="flex items-center gap-2 text-[11px] font-semibold uppercase tracking-wider text-muted-foreground">
                  <CheckCircle2 className="size-3.5" aria-hidden="true" />
                  Conclusão
                </p>
                <p className="mt-2 text-base font-semibold leading-relaxed">{selected.answer}</p>
                <div className="mt-3 flex flex-wrap gap-1.5">
                  {selected.evidence.map((evidence) => <Badge key={evidence} variant="secondary">{evidence}</Badge>)}
                </div>
              </div>

              <div className="rounded-xl border border-border bg-muted/35 p-3">
                <p className="flex items-center gap-2 text-xs font-semibold">
                  <ShieldCheck className="size-4 text-primary" aria-hidden="true" />
                  Limite declarado
                </p>
                <p className="mt-1 text-xs leading-relaxed text-muted-foreground">{selected.limitation}</p>
              </div>
            </CardContent>
          </Card>
        </Reveal>
      </div>
    </section>
  )
}
