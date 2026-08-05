import { useEffect, useMemo, useRef } from 'react'
import { createTimer, utils } from 'animejs'

import { cn } from '@/lib/utils'
import type { DiscoveredApplication } from '@/lib/api'

const CENTER = 120
const RINGS = [27, 54, 81, 108]
const SWEEP_MS = 4200
/** Degrees of afterglow trailing the sweep line. */
const TRAIL = 70

type Blip = {
  name: string
  angle: number
  radius: number
  x: number
  y: number
  tone: 'ok' | 'warn' | 'idle'
  ringLabel: string
}

/** Stable pseudo-angle from the name so a blip never jumps between renders. */
function angleFor(name: string) {
  let hash = 0
  for (let i = 0; i < name.length; i++) hash = (hash * 31 + name.charCodeAt(i)) % 3600
  return hash / 10
}

/**
 * Distance from the centre encodes how much the MCP actually knows about an
 * app: fully indexed sits inside, trace-only sightings sit out on the rim.
 */
function depthFor(app: DiscoveredApplication) {
  if (app.openApi.validated && app.enabled) return { radius: 34, ringLabel: 'indexada' }
  if (app.baseUrl) return { radius: 66, ringLabel: 'com endpoint, sem spec válida' }
  return { radius: 97, ringLabel: 'vista apenas por traces' }
}

function toneFor(app: DiscoveredApplication): Blip['tone'] {
  if (!app.enabled || app.missing) return 'idle'
  if (app.health?.allReady) return 'ok'
  return 'warn'
}

const TONE_FILL: Record<Blip['tone'], string> = {
  ok: 'var(--success)',
  warn: 'var(--warning)',
  idle: 'var(--muted-foreground)',
}

export function RadarScope({
  applications,
  className,
}: {
  applications: DiscoveredApplication[]
  className?: string
}) {
  const sweepRef = useRef<SVGGElement | null>(null)
  const blipRefs = useRef<Array<SVGCircleElement | null>>([])
  const haloRefs = useRef<Array<SVGCircleElement | null>>([])

  const blips = useMemo<Blip[]>(
    () =>
      applications.map((app) => {
        const angle = angleFor(app.name)
        const { radius, ringLabel } = depthFor(app)
        const rad = ((angle - 90) * Math.PI) / 180
        return {
          name: app.name,
          angle,
          radius,
          x: CENTER + Math.cos(rad) * radius,
          y: CENTER + Math.sin(rad) * radius,
          tone: toneFor(app),
          ringLabel,
        }
      }),
    [applications],
  )

  useEffect(() => {
    const reduced = window.matchMedia('(prefers-reduced-motion: reduce)').matches

    // Reduced motion: park the sweep and show every blip at rest, fully lit.
    if (reduced) {
      if (sweepRef.current) utils.set(sweepRef.current, { rotate: 45 })
      blipRefs.current.forEach((el) => el && utils.set(el, { opacity: 1, scale: 1 }))
      haloRefs.current.forEach((el) => el && utils.set(el, { opacity: 0.25, scale: 1.8 }))
      return
    }

    // One timer drives both the sweep and every blip, so the glow is genuinely
    // synced to the beam rather than just looping near it.
    const timer = createTimer({
      duration: SWEEP_MS,
      loop: true,
      onUpdate: (self) => {
        // iterationProgress, not progress: with loop:true the total duration is
        // infinite, so `progress` sits at ~0 forever and the sweep never moves.
        const sweep = self.iterationProgress * 360
        if (sweepRef.current) utils.set(sweepRef.current, { rotate: sweep })

        blips.forEach((blip, i) => {
          const since = (sweep - blip.angle + 360) % 360
          const glow = since < TRAIL ? 1 - since / TRAIL : 0
          const el = blipRefs.current[i]
          const halo = haloRefs.current[i]
          if (el) utils.set(el, { opacity: 0.4 + glow * 0.6, scale: 1 + glow * 0.7 })
          if (halo) utils.set(halo, { opacity: glow * 0.35, scale: 1 + glow * 2.4 })
        })
      },
    })

    return () => {
      timer.revert()
    }
  }, [blips])

  const summary =
    blips.length === 0
      ? 'Radar de descoberta sem aplicações detectadas no momento.'
      : `Radar de descoberta: ${blips.length} aplicação(ões) detectada(s) — ${blips
          .map((b) => `${b.name} (${b.ringLabel})`)
          .join(', ')}.`

  return (
    <div className={cn('relative', className)}>
      <svg viewBox="0 0 240 240" role="img" aria-label={summary} className="w-full">
        <defs>
          <radialGradient id="radar-bg" cx="50%" cy="50%" r="50%">
            <stop offset="0%" stopColor="var(--chart-1)" stopOpacity="0.16" />
            <stop offset="70%" stopColor="var(--chart-1)" stopOpacity="0.05" />
            <stop offset="100%" stopColor="var(--chart-1)" stopOpacity="0" />
          </radialGradient>
          {/* Wedge fades from the beam edge backwards, giving the sweep a tail. */}
          <linearGradient id="radar-sweep" x1="0" y1="0" x2="1" y2="0">
            <stop offset="0%" stopColor="var(--chart-2)" stopOpacity="0" />
            <stop offset="100%" stopColor="var(--chart-2)" stopOpacity="0.35" />
          </linearGradient>
        </defs>

        <circle cx={CENTER} cy={CENTER} r={RINGS[3]} fill="url(#radar-bg)" />

        {RINGS.map((r) => (
          <circle
            key={r}
            cx={CENTER}
            cy={CENTER}
            r={r}
            fill="none"
            stroke="var(--border)"
            strokeWidth="1"
          />
        ))}

        <line x1={CENTER - 108} y1={CENTER} x2={CENTER + 108} y2={CENTER} stroke="var(--border)" strokeWidth="1" />
        <line x1={CENTER} y1={CENTER - 108} x2={CENTER} y2={CENTER + 108} stroke="var(--border)" strokeWidth="1" />

        <g ref={sweepRef} style={{ transformOrigin: `${CENTER}px ${CENTER}px` }}>
          <path
            d={`M ${CENTER} ${CENTER} L ${CENTER} ${CENTER - RINGS[3]} A ${RINGS[3]} ${RINGS[3]} 0 0 1 ${
              CENTER + RINGS[3] * Math.cos(-Math.PI / 3)
            } ${CENTER + RINGS[3] * Math.sin(-Math.PI / 3)} Z`}
            fill="url(#radar-sweep)"
          />
          <line
            x1={CENTER}
            y1={CENTER}
            x2={CENTER}
            y2={CENTER - RINGS[3]}
            stroke="var(--chart-2)"
            strokeWidth="1.5"
            strokeOpacity="0.8"
          />
        </g>

        {blips.map((blip, i) => (
          <g key={blip.name} transform={`translate(${blip.x} ${blip.y})`}>
            <circle
              ref={(el) => {
                haloRefs.current[i] = el
              }}
              r="5"
              fill={TONE_FILL[blip.tone]}
              opacity="0"
            />
            <circle
              ref={(el) => {
                blipRefs.current[i] = el
              }}
              r="3.5"
              fill={TONE_FILL[blip.tone]}
              opacity="0.4"
            />
          </g>
        ))}

        <circle cx={CENTER} cy={CENTER} r="3" fill="var(--primary)" />
      </svg>

      {blips.length === 0 && (
        <p className="absolute inset-x-0 bottom-1/2 text-center text-xs text-muted-foreground">
          Varrendo o cluster…
        </p>
      )}
    </div>
  )
}

/**
 * Text counterpart to the scope — the radar alone is not readable content.
 * The scope encodes two independent dimensions, so the key has to as well:
 * distance = how much the MCP knows, colour = current health.
 */
export function RadarLegend({ applications }: { applications: DiscoveredApplication[] }) {
  const depth = [
    { label: 'Indexadas — spec OpenAPI validada', hint: 'anel interno', value: applications.filter((a) => a.openApi.validated && a.enabled).length },
    { label: 'Com endpoint, sem spec válida', hint: 'anel médio', value: applications.filter((a) => a.baseUrl && !(a.openApi.validated && a.enabled)).length },
    { label: 'Vistas apenas por traces', hint: 'borda', value: applications.filter((a) => !a.baseUrl).length },
  ]

  const health = [
    { label: 'Saudável', tone: 'bg-success', value: applications.filter((a) => a.enabled && !a.missing && a.health?.allReady).length },
    { label: 'Degradada', tone: 'bg-warning', value: applications.filter((a) => a.enabled && !a.missing && !a.health?.allReady).length },
    { label: 'Não indexada', tone: 'bg-muted-foreground', value: applications.filter((a) => !a.enabled || a.missing).length },
  ]

  return (
    <div className="space-y-3">
      <div>
        <p className="mb-1.5 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
          Distância do centro
        </p>
        <ul className="space-y-1 text-xs">
          {depth.map((row, i) => (
            <li key={row.label} className="flex items-center gap-2">
              {/* Ring position, drawn as a dot pushed progressively outward. */}
              <span className="relative size-2.5 shrink-0" aria-hidden="true">
                <span className="absolute inset-0 rounded-full border border-border" />
                <span
                  className="absolute size-1 rounded-full bg-foreground/70"
                  style={{ left: `${1 + i * 3.5}px`, top: '3px' }}
                />
              </span>
              <span className="flex-1 text-muted-foreground">{row.label}</span>
              <span className="font-medium tabular">{row.value}</span>
            </li>
          ))}
        </ul>
      </div>

      <div>
        <p className="mb-1.5 text-[10px] font-semibold uppercase tracking-wider text-muted-foreground">
          Cor do ponto
        </p>
        <ul className="flex flex-wrap gap-x-4 gap-y-1 text-xs">
          {health.map((row) => (
            <li key={row.label} className="flex items-center gap-1.5">
              <span className={cn('size-2 shrink-0 rounded-full', row.tone)} aria-hidden="true" />
              <span className="text-muted-foreground">{row.label}</span>
              <span className="font-medium tabular">{row.value}</span>
            </li>
          ))}
        </ul>
      </div>
    </div>
  )
}
