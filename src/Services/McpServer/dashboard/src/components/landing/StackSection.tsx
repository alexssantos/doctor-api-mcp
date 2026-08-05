import { BarChart3, ExternalLink, Flame, ScrollText, Waypoints } from 'lucide-react'
import type { ComponentType } from 'react'

import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Reveal } from '@/components/ui/reveal'
import { SectionHeading } from '@/components/landing/SectionHeading'
import type { DashboardLinks } from '@/lib/api'

interface StackItem {
  key: string
  label: string
  role: string
  port: string
  icon: ComponentType<{ className?: string }>
  tone: string
}

const STACK: StackItem[] = [
  { key: 'jaeger', label: 'Jaeger', role: 'Traces distribuídos (OTLP)', port: '16686', icon: Waypoints, tone: 'text-chart-1' },
  { key: 'prometheus', label: 'Prometheus', role: 'Métricas e séries temporais', port: '9090', icon: Flame, tone: 'text-chart-3' },
  { key: 'grafana', label: 'Grafana', role: 'Dashboards visuais', port: '3000', icon: BarChart3, tone: 'text-chart-4' },
  { key: 'loki', label: 'Loki + Promtail', role: 'Agregação de logs', port: '—', icon: ScrollText, tone: 'text-chart-2' },
]

const TECH = [
  '.NET 9',
  'ASP.NET Minimal APIs',
  'MCP C# SDK',
  'Kubernetes (k3d)',
  'Helm',
  'Tilt',
  'OpenTelemetry',
  'PostgreSQL',
  'React 19',
  'Tailwind CSS v4',
]

export function StackSection({ links }: { links?: DashboardLinks }) {
  return (
    <section aria-labelledby="stack-heading">
      <SectionHeading
        id="stack-heading"
        eyebrow="Stack"
        title="Abra as fontes por trás do radar"
        description="As mesmas instâncias que alimentam as ferramentas MCP — acessíveis direto, quando você quiser conferir na mão."
      />

      <div className="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
        {STACK.map((item, i) => {
          const Icon = item.icon
          const href = links?.[item.key] ?? links?.[item.label]

          const body = (
            <Card className="h-full transition-colors group-hover:border-primary/40">
              <CardContent className="flex h-full flex-col gap-2 p-4">
                <div className="flex items-center justify-between gap-2">
                  <Icon className={`size-5 ${item.tone}`} />
                  {href && (
                    <ExternalLink className="size-3.5 text-muted-foreground opacity-0 transition-opacity group-hover:opacity-100 group-focus-visible:opacity-100" />
                  )}
                </div>
                <div>
                  <p className="text-sm font-medium">{item.label}</p>
                  <p className="mt-0.5 text-xs text-muted-foreground">{item.role}</p>
                </div>
                {item.port !== '—' && (
                  <Badge variant="outline" className="tabular">
                    :{item.port}
                  </Badge>
                )}
              </CardContent>
            </Card>
          )

          return (
            <Reveal key={item.key} delay={i * 60}>
              {href ? (
                <a
                  href={href}
                  target="_blank"
                  rel="noreferrer noopener"
                  className="group block h-full rounded-xl outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
                >
                  {body}
                  <span className="sr-only">(abre em nova aba)</span>
                </a>
              ) : (
                <div className="group h-full">{body}</div>
              )}
            </Reveal>
          )
        })}
      </div>

      <Reveal delay={120} className="mt-4">
        <div className="flex flex-wrap gap-1.5">
          {TECH.map((tech) => (
            <Badge key={tech} variant="secondary">
              {tech}
            </Badge>
          ))}
        </div>
      </Reveal>
    </section>
  )
}
