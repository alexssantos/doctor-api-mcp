import { Boxes, HeartPulse, Layers, Server } from 'lucide-react'
import type { ComponentType } from 'react'

import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { cn } from '@/lib/utils'
import type { ClusterSummary } from '@/lib/api'

/** Donut gauge — reads faster than a bare percentage at a glance. */
function HealthRing({ pct }: { pct: number }) {
  const radius = 16
  const circumference = 2 * Math.PI * radius
  const tone = pct >= 90 ? 'text-success-fg' : pct >= 60 ? 'text-warning-fg' : 'text-destructive'

  return (
    <svg viewBox="0 0 40 40" className="size-10 -rotate-90" aria-hidden="true">
      <circle cx="20" cy="20" r={radius} fill="none" strokeWidth="4" className="stroke-muted" />
      <circle
        cx="20"
        cy="20"
        r={radius}
        fill="none"
        strokeWidth="4"
        strokeLinecap="round"
        strokeDasharray={circumference}
        strokeDashoffset={circumference * (1 - pct / 100)}
        className={cn('stroke-current transition-[stroke-dashoffset] duration-500', tone)}
      />
    </svg>
  )
}

function StatCard({
  label,
  value,
  icon: Icon,
  hint,
  isLoading,
  accent,
  ring,
}: {
  label: string
  value: string
  icon: ComponentType<{ className?: string }>
  hint?: string
  isLoading?: boolean
  accent: string
  ring?: number | null
}) {
  return (
    <Card>
      <CardContent className="flex items-start justify-between gap-3 p-4">
        <div className="min-w-0">
          <p className="text-xs font-medium text-muted-foreground">{label}</p>
          {isLoading ? (
            <Skeleton className="mt-2 h-7 w-16" />
          ) : (
            <p className="mt-1 text-2xl font-semibold tracking-tight tabular">{value}</p>
          )}
          {hint && <p className="mt-1 text-xs text-muted-foreground tabular">{hint}</p>}
        </div>
        {ring != null && !isLoading ? (
          <HealthRing pct={ring} />
        ) : (
          <div className={`flex size-10 shrink-0 items-center justify-center rounded-lg ${accent}`}>
            <Icon className="size-5" />
          </div>
        )}
      </CardContent>
    </Card>
  )
}

export function StatsCards({
  cluster,
  applicationsCount,
  enabledCount,
  isLoading,
}: {
  cluster?: ClusterSummary
  applicationsCount: number
  enabledCount: number
  isLoading: boolean
}) {
  const podsReadyPct =
    cluster && cluster.totalPods > 0 ? Math.round((cluster.readyPods / cluster.totalPods) * 100) : null

  return (
    <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
      <StatCard
        label="Aplicações descobertas"
        value={String(applicationsCount)}
        hint={`${enabledCount} habilitada${enabledCount !== 1 ? 's' : ''} para o MCP`}
        icon={Server}
        isLoading={isLoading}
        accent="bg-chart-1/15 text-chart-1"
      />
      <StatCard
        label="Pods prontos"
        value={cluster ? `${cluster.readyPods}/${cluster.totalPods}` : '—'}
        hint={podsReadyPct !== null ? `${podsReadyPct}% saudável` : undefined}
        icon={Boxes}
        isLoading={isLoading}
        accent="bg-chart-2/15 text-chart-2"
      />
      <StatCard
        label="Deployments prontos"
        value={cluster ? `${cluster.readyDeployments}/${cluster.totalDeployments}` : '—'}
        icon={Layers}
        isLoading={isLoading}
        accent="bg-chart-4/15 text-chart-4"
      />
      <StatCard
        label="Saúde do cluster"
        value={podsReadyPct !== null ? `${podsReadyPct}%` : '—'}
        hint="Pods prontos / total"
        icon={HeartPulse}
        isLoading={isLoading}
        accent="bg-success/15 text-success-fg"
        ring={podsReadyPct}
      />
    </div>
  )
}
