import { useEffect, useRef } from 'react'
import { animate, onScroll, stagger, utils } from 'animejs'
import { Activity, Boxes, Brain, FileJson, Flame, ScrollText, Waypoints } from 'lucide-react'
import type { ComponentType } from 'react'

import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Reveal } from '@/components/ui/reveal'
import { SectionHeading } from '@/components/landing/SectionHeading'

interface Signal {
  icon: ComponentType<{ className?: string }>
  source: string
  title: string
  body: string
  tone: string
  status: 'ativo' | 'em breve'
}

const SIGNALS: Signal[] = [
  {
    icon: Waypoints,
    source: 'Jaeger',
    title: 'Traces',
    body: 'Call chain completa de cada requisição: quem chamou quem, com timings e queries SQL no caminho.',
    tone: 'text-chart-2',
    status: 'ativo',
  },
  {
    icon: Flame,
    source: 'Prometheus',
    title: 'Métricas',
    body: 'Séries temporais por serviço — taxa de requisições, erros 5xx, memória. PromQL arbitrário liberado ao agente.',
    tone: 'text-chart-3',
    status: 'ativo',
  },
  {
    icon: Boxes,
    source: 'Kubernetes',
    title: 'Estado do cluster',
    body: 'Pods, deployments, restarts e prontidão lidos in-cluster via RBAC read-only.',
    tone: 'text-chart-1',
    status: 'ativo',
  },
  {
    icon: FileJson,
    source: 'OpenAPI',
    title: 'Contratos',
    body: 'Rotas e schemas de cada aplicação — a base para explicar o que uma API faz.',
    tone: 'text-chart-4',
    status: 'ativo',
  },
  {
    icon: ScrollText,
    source: 'Loki',
    title: 'Logs',
    body: 'Loki e Promtail já rodam no cluster, mas ainda não são lidos pelas ferramentas MCP.',
    tone: 'text-muted-foreground',
    status: 'em breve',
  },
]

/** Dots travelling from each signal card down into the correlation box. */
function DataStream() {
  const ref = useRef<SVGSVGElement | null>(null)

  useEffect(() => {
    const el = ref.current
    if (!el) return
    const dots = el.querySelectorAll('circle')

    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      utils.set(dots, { opacity: 0.4 })
      return
    }

    const animation = animate(dots, {
      // Each dot rides its own column down into the funnel.
      cy: [4, 44],
      opacity: [{ to: 0.9, duration: 200 }, { to: 0, duration: 500 }],
      duration: 1400,
      delay: stagger(220),
      loop: true,
      ease: 'inOut(2)',
      autoplay: onScroll({ enter: 'bottom top', leave: 'top bottom' }),
    })

    return () => {
      animation.revert()
    }
  }, [])

  return (
    <svg ref={ref} viewBox="0 0 100 48" preserveAspectRatio="none" className="h-12 w-full" aria-hidden="true">
      {[10, 30, 50, 70, 90].map((x) => (
        <line key={x} x1={x} y1="0" x2={x} y2="48" stroke="var(--border)" strokeWidth="0.4" />
      ))}
      {[10, 30, 50, 70, 90].map((x) => (
        <circle key={x} cx={x} cy="4" r="1.8" fill="var(--chart-2)" opacity="0" />
      ))}
    </svg>
  )
}

export function SignalsSection() {
  return (
    <section aria-labelledby="sinais-heading">
      <SectionHeading
        id="sinais-heading"
        eyebrow="Sinais"
        title="Tudo o que o radar capta, correlacionado"
        description="Cada sinal sozinho é ruído. O MCP cruza traces, métricas, estado do cluster e contratos na mesma resposta, para o modelo raciocinar sobre o sistema — não sobre planilhas separadas."
      />

      <div className="mt-6 grid grid-cols-2 gap-3 lg:grid-cols-5">
        {SIGNALS.map((signal, i) => {
          const Icon = signal.icon
          const soon = signal.status === 'em breve'
          return (
            <Reveal key={signal.title} delay={i * 60}>
              <Card className={soon ? 'h-full border-dashed bg-muted/20' : 'h-full'}>
                <CardContent className="flex h-full flex-col gap-2 p-4">
                  <div className="flex items-center justify-between gap-2">
                    <Icon className={`size-5 ${signal.tone}`} />
                    <Badge variant={soon ? 'outline' : 'success'} className="text-[10px]">
                      {signal.status}
                    </Badge>
                  </div>
                  <div>
                    <p className={`text-sm font-semibold ${soon ? 'text-muted-foreground' : ''}`}>{signal.title}</p>
                    <p className="font-mono text-[11px] text-muted-foreground">{signal.source}</p>
                  </div>
                  <p className="text-[11px] leading-relaxed text-muted-foreground">{signal.body}</p>
                </CardContent>
              </Card>
            </Reveal>
          )
        })}
      </div>

      <DataStream />

      <Reveal>
        <Card className="border-primary/40 bg-primary/5">
          <CardContent className="flex flex-col items-center gap-3 p-5 text-center sm:flex-row sm:text-left">
            <div className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-primary/15 text-primary">
              <Brain className="size-5" />
            </div>
            <div className="flex-1">
              <p className="text-sm font-semibold">Correlação no MCP Server</p>
              <p className="mt-0.5 text-xs leading-relaxed text-muted-foreground">
                <span className="font-mono">find_data_origin</span> junta rota, chamadas HTTP e queries SQL num
                caminho só. <span className="font-mono">explain_api</span> cruza a spec com tráfego real. O agente
                recebe a conclusão, não os dados brutos.
              </p>
            </div>
            <Activity className="hidden size-4 shrink-0 text-primary sm:block" />
          </CardContent>
        </Card>
      </Reveal>
    </section>
  )
}
