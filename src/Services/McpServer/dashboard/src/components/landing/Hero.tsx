import { useEffect, useRef } from 'react'
import { animate, onScroll, stagger, utils } from 'animejs'
import { ArrowRight, Plug, Radar, ScanLine, Sparkles } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { RadarLegend, RadarScope } from '@/components/landing/RadarScope'
import type { ClusterSummary, DiscoveredApplication } from '@/lib/api'

/** Counts up to `value` on first scroll into view. */
function LiveStat({ value, label, suffix }: { value: number; label: string; suffix?: string }) {
  const ref = useRef<HTMLParagraphElement | null>(null)

  useEffect(() => {
    const el = ref.current
    if (!el) return

    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      el.textContent = `${value}${suffix ?? ''}`
      return
    }

    const counter = { n: 0 }
    const animation = animate(counter, {
      n: value,
      duration: 900,
      ease: 'out(3)',
      onUpdate: () => {
        el.textContent = `${Math.round(counter.n)}${suffix ?? ''}`
      },
      autoplay: onScroll({ enter: 'bottom-=40 top', repeat: false }),
    })

    return () => {
      animation.revert()
    }
  }, [value, suffix])

  return (
    <div className="rounded-xl border border-border bg-card/60 px-3 py-2.5 backdrop-blur">
      <p ref={ref} className="text-xl font-semibold tracking-tight tabular">
        0{suffix ?? ''}
      </p>
      <p className="mt-0.5 text-[11px] leading-tight text-muted-foreground">{label}</p>
    </div>
  )
}

export function Hero({
  cluster,
  applications,
  enabledCount,
  onNavigate,
}: {
  cluster?: ClusterSummary
  applications: DiscoveredApplication[]
  enabledCount: number
  onNavigate: (tab: string) => void
}) {
  const copyRef = useRef<HTMLDivElement | null>(null)

  // Headline block animates in on mount — it is above the fold, so waiting for
  // a scroll trigger would leave it invisible.
  useEffect(() => {
    const root = copyRef.current
    if (!root) return
    const items = root.querySelectorAll('[data-hero-item]')

    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      utils.set(items, { opacity: 1, y: 0 })
      return
    }

    const animation = animate(items, {
      opacity: [0, 1],
      y: [20, 0],
      duration: 700,
      delay: stagger(90),
      ease: 'out(3)',
    })

    return () => {
      animation.revert()
    }
  }, [])

  const podsPct =
    cluster && cluster.totalPods > 0 ? Math.round((cluster.readyPods / cluster.totalPods) * 100) : 0

  return (
    <section className="relative overflow-hidden rounded-2xl border border-border bg-card">
      <div
        aria-hidden="true"
        className="pointer-events-none absolute inset-0 bg-[radial-gradient(60%_80%_at_15%_0%,var(--color-chart-1)_0%,transparent_60%),radial-gradient(50%_70%_at_90%_10%,var(--color-chart-2)_0%,transparent_55%)] opacity-[0.10] dark:opacity-[0.16]"
      />

      <div className="relative grid gap-8 px-6 py-10 sm:px-10 sm:py-14 lg:grid-cols-[1.15fr_1fr] lg:items-center lg:gap-12">
        <div ref={copyRef}>
          <Badge variant="secondary" className="mb-4 opacity-0" data-hero-item>
            <Radar className="size-3" />
            Descoberta contínua · Model Context Protocol
          </Badge>

          <h2
            className="text-balance text-3xl font-bold leading-tight tracking-tight opacity-0 sm:text-4xl lg:text-[2.75rem]"
            data-hero-item
          >
            Um radar sobre o cluster que{' '}
            <span className="bg-gradient-to-r from-chart-1 to-chart-2 bg-clip-text text-transparent">
              entrega contexto pronto para a IA
            </span>
          </h2>

          <p
            className="mt-4 max-w-xl text-pretty text-sm leading-relaxed text-muted-foreground opacity-0 sm:text-base"
            data-hero-item
          >
            O <strong className="font-semibold text-foreground">mcp-apis</strong> varre o cluster sozinho,
            encontra cada aplicação e correlaciona <strong className="font-medium text-foreground">traces</strong>,{' '}
            <strong className="font-medium text-foreground">métricas</strong> e{' '}
            <strong className="font-medium text-foreground">estado dos pods</strong> em respostas únicas. O agente
            pergunta em linguagem natural e recebe status e análise — não um dump de dados para interpretar.
          </p>

          <div className="mt-6 flex flex-wrap items-center gap-3 opacity-0" data-hero-item>
            <Button onClick={() => onNavigate('operacao')}>
              Ver o radar em operação
              <ArrowRight className="size-4" />
            </Button>
            <Button variant="outline" asChild>
              <a
                href="#conectar"
                onClick={(e) => {
                  const target = document.getElementById('conectar')
                  if (!target) return
                  e.preventDefault()
                  target.scrollIntoView({ behavior: 'smooth', block: 'start' })
                }}
              >
                <Plug className="size-4" />
                Conectar um agente
              </a>
            </Button>
          </div>

          <div className="mt-8 grid grid-cols-2 gap-2.5 opacity-0 sm:grid-cols-4" data-hero-item>
            <LiveStat value={applications.length} label="aplicações no radar" />
            <LiveStat value={enabledCount} label="indexadas para o MCP" />
            <LiveStat value={podsPct} suffix="%" label="pods prontos" />
            <LiveStat value={9} label="ferramentas MCP" />
          </div>
        </div>

        <div className="mx-auto w-full max-w-sm lg:max-w-none">
          <div className="mb-3 flex items-center justify-center gap-2 lg:justify-start">
            <ScanLine className="size-3.5 text-chart-2" />
            <p className="text-xs font-medium text-muted-foreground">Varredura ao vivo deste cluster</p>
          </div>

          <RadarScope applications={applications} />

          <div className="mt-4 rounded-xl border border-border bg-card/60 p-3 backdrop-blur">
            <p className="mb-2.5 flex items-center gap-1.5 text-[11px] font-medium">
              <Sparkles className="size-3 text-chart-1" />
              Como ler o radar
            </p>
            <RadarLegend applications={applications} />
          </div>
        </div>
      </div>
    </section>
  )
}
