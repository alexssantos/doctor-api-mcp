import { BarChart3, ExternalLink, Flame, Link2, Waypoints } from 'lucide-react'
import type { ComponentType } from 'react'

import { Card, CardContent } from '@/components/ui/card'
import type { DashboardLinks } from '@/lib/api'

const LINK_META: Record<string, { label: string; description: string; icon: ComponentType<{ className?: string }>; accent: string }> = {
  jaeger: {
    label: 'Jaeger',
    description: 'Rastreamento distribuído (traces)',
    icon: Waypoints,
    accent: 'text-chart-1',
  },
  prometheus: {
    label: 'Prometheus',
    description: 'Métricas e séries temporais',
    icon: Flame,
    accent: 'text-chart-3',
  },
  grafana: {
    label: 'Grafana',
    description: 'Dashboards visuais',
    icon: BarChart3,
    accent: 'text-chart-4',
  },
}

export function QuickLinks({ links }: { links?: DashboardLinks }) {
  const entries = Object.entries(links ?? {})

  if (entries.length === 0) {
    return null
  }

  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
      {entries.map(([key, url]) => {
        const meta = LINK_META[key.toLowerCase()] ?? {
          label: key,
          description: 'Serviço conectado',
          icon: Link2,
          accent: 'text-muted-foreground',
        }
        const Icon = meta.icon

        return (
          <a
            key={key}
            href={url}
            target="_blank"
            rel="noreferrer noopener"
            className="group rounded-xl outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background"
          >
            <Card className="h-full transition-colors group-hover:border-primary/50 group-hover:bg-accent/40">
              <CardContent className="flex items-center gap-3 p-4">
                <div className="flex size-10 shrink-0 items-center justify-center rounded-lg bg-muted">
                  <Icon className={`size-5 ${meta.accent}`} />
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex items-center gap-1.5">
                    <p className="truncate text-sm font-medium">{meta.label}</p>
                    <ExternalLink className="size-3.5 shrink-0 text-muted-foreground opacity-0 transition-opacity group-hover:opacity-100 group-focus-visible:opacity-100" />
                  </div>
                  <p className="truncate text-xs text-muted-foreground">{meta.description}</p>
                </div>
              </CardContent>
            </Card>
            <span className="sr-only">(abre em nova aba)</span>
          </a>
        )
      })}
    </div>
  )
}
