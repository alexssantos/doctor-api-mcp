import { useEffect, useRef, type RefObject } from 'react'
import { animate, onScroll, svg, utils } from 'animejs'
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
  status: 'ativo'
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
    body: 'RED, CPU e memória consultados por descritores internos limitados; o agente não envia PromQL arbitrário.',
    tone: 'text-chart-3',
    status: 'ativo',
  },
  {
    icon: Boxes,
    source: 'Kubernetes',
    title: 'Estado do cluster',
    body: 'Pods, deployments, restarts, revisões e Events lidos in-cluster via RBAC e allowlist.',
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
    body: 'Padrões e fingerprints internos correlacionados à timeline, com redaction de segredos e PII.',
    tone: 'text-chart-2',
    status: 'ativo',
  },
]

const SIGNAL_X = [10, 30, 50, 70, 90]
const TRAVEL_MS = 1200
const STAGGER_MS = TRAVEL_MS / SIGNAL_X.length

/** Alternates the curve's bow left/right per column so the cascade reads as a little wave. */
function signalPathD(x: number, i: number) {
  const bow = i % 2 === 0 ? 3 : -3
  return `M ${x} 4 C ${x + bow} 18 ${x - bow} 30 ${x} 44`
}

/**
 * Each signal card rides its own curved motion path down into the correlation box, one after
 * another in a staggered cascade (not all five at once). The correlation box's outline flashes
 * on and fades on every arrival, in the same order.
 */
function SignalCascade({ highlightRef }: { highlightRef: RefObject<HTMLDivElement | null> }) {
  const ref = useRef<SVGSVGElement | null>(null)

  useEffect(() => {
    const el = ref.current
    const highlight = highlightRef.current
    if (!el || !highlight) return

    const balls = SIGNAL_X.map((_, i) => el.querySelector<SVGCircleElement>(`#signal-ball-${i}`))
    const paths = SIGNAL_X.map((_, i) => el.querySelector<SVGPathElement>(`#signal-path-${i}`))
    if (balls.some((ball) => !ball) || paths.some((path) => !path)) return

    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      // Park each ball at rest on its own path instead of leaving it invisible.
      balls.forEach((ball, i) => utils.set(ball!, { translateX: SIGNAL_X[i], translateY: 4, opacity: 0.7 }))
      utils.set(highlight, { opacity: 0.5 })
      return
    }

    const animations = paths.map((path, i) =>
      animate(balls[i]!, {
        // Rides its own dashed curve; rotate is unused (the ball is round) but harmless.
        ...svg.createMotionPath(path!),
        opacity: [
          { to: 0, duration: 0 },
          { to: 1, duration: 140 },
          { to: 1, duration: TRAVEL_MS - 280 },
          { to: 0, duration: 140 },
        ],
        duration: TRAVEL_MS,
        delay: i * STAGGER_MS,
        loop: true,
        ease: 'inOutSine',
        autoplay: onScroll({ enter: 'bottom top', leave: 'top bottom' }),
      }),
    )

    // Flashes the correlation box's outline on each arrival, evenly spaced across one TRAVEL_MS
    // loop (5 pulses at 0/STAGGER/2*STAGGER/3*STAGGER/4*STAGGER - matches the balls' cascade above).
    const pulse = animate(highlight, {
      opacity: [
        { to: 1, duration: 45 }, { to: 0, duration: 45 }, { to: 0, duration: STAGGER_MS - 90 },
        { to: 1, duration: 45 }, { to: 0, duration: 45 }, { to: 0, duration: STAGGER_MS - 90 },
        { to: 1, duration: 45 }, { to: 0, duration: 45 }, { to: 0, duration: STAGGER_MS - 90 },
        { to: 1, duration: 45 }, { to: 0, duration: 45 }, { to: 0, duration: STAGGER_MS - 90 },
        { to: 1, duration: 45 }, { to: 0, duration: 45 }, { to: 0, duration: STAGGER_MS - 90 },
      ],
      duration: TRAVEL_MS,
      loop: true,
      ease: 'linear',
      autoplay: onScroll({ enter: 'bottom top', leave: 'top bottom' }),
    })

    return () => {
      animations.forEach((animation) => animation.revert())
      pulse.revert()
    }
  }, [highlightRef])

  return (
    <svg ref={ref} viewBox="0 0 100 48" preserveAspectRatio="none" className="h-12 w-full" aria-hidden="true">
      {SIGNAL_X.map((x, i) => (
        <path
          key={x}
          id={`signal-path-${i}`}
          d={signalPathD(x, i)}
          fill="none"
          stroke="var(--border)"
          strokeWidth="0.6"
          strokeDasharray="1.6 2.2"
          strokeLinecap="round"
        />
      ))}
      {SIGNAL_X.map((x, i) => (
        <circle
          key={x}
          id={`signal-ball-${i}`}
          r="2.2"
          fill="var(--chart-2)"
          opacity="0"
          style={{ filter: 'drop-shadow(0 0 3px var(--chart-2))' }}
        />
      ))}
    </svg>
  )
}

export function SignalsSection() {
  const correlationHighlightRef = useRef<HTMLDivElement | null>(null)

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
          return (
            <Reveal key={signal.title} delay={i * 60}>
              <Card className="h-full">
                <CardContent className="flex h-full flex-col gap-2 p-4">
                  <div className="flex items-center justify-between gap-2">
                    <Icon className={`size-5 ${signal.tone}`} />
                    <Badge variant="success" className="text-[10px]">
                      {signal.status}
                    </Badge>
                  </div>
                  <div>
                    <p className="text-sm font-semibold">{signal.title}</p>
                    <p className="font-mono text-[11px] text-muted-foreground">{signal.source}</p>
                  </div>
                  <p className="text-[11px] leading-relaxed text-muted-foreground">{signal.body}</p>
                </CardContent>
              </Card>
            </Reveal>
          )
        })}
      </div>

      <SignalCascade highlightRef={correlationHighlightRef} />

      <Reveal>
        <Card className="relative border-primary/40 bg-primary/5">
          <div
            ref={correlationHighlightRef}
            aria-hidden="true"
            className="pointer-events-none absolute inset-0 rounded-xl opacity-0"
            style={{ boxShadow: 'inset 0 0 0 2px var(--chart-2), 0 0 24px var(--chart-2)' }}
          />
          <CardContent className="flex flex-col items-center gap-3 p-5 text-center sm:flex-row sm:text-left">
            <div className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-primary/15 text-primary">
              <Brain className="size-5" />
            </div>
            <div className="flex-1">
              <p className="text-sm font-semibold">Correlação no MCP Server</p>
              <p className="mt-0.5 text-xs leading-relaxed text-muted-foreground">
                Health, Dependency, Anomaly, Correlation e RCA Engines trabalham sobre providers normalizados.
                O agente recebe conclusão, evidências e limitações — nunca consultas raw ou causalidade inventada.
              </p>
            </div>
            <Activity className="hidden size-4 shrink-0 text-primary sm:block" />
          </CardContent>
        </Card>
      </Reveal>
    </section>
  )
}
