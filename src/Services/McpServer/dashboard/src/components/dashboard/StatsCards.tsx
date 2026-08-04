import { Boxes, HeartPulse, Layers, Server } from 'lucide-react'
import type { ComponentType } from 'react'

import { Card, CardContent } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import type { ClusterSummary } from '@/lib/api'

function StatCard({
  label,
  value,
  icon: Icon,
  hint,
  isLoading,
  accent,
}: {
  label: string
  value: string
  icon: ComponentType<{ className?: string }>
  hint?: string
  isLoading?: boolean
  accent: string
}) {
  return (
    <Card>
      <CardContent className="flex items-start justify-between gap-3 p-5">
        <div className="min-w-0">
          <p className="text-xs font-medium text-muted-foreground">{label}</p>
          {isLoading ? (
            <Skeleton className="mt-2 h-7 w-16" />
          ) : (
            <p className="mt-1 text-2xl font-semibold tracking-tight">{value}</p>
          )}
          {hint && <p className="mt-1 text-xs text-muted-foreground">{hint}</p>}
        </div>
        <div className={`flex size-10 shrink-0 items-center justify-center rounded-lg ${accent}`}>
          <Icon className="size-5" />
        </div>
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
  const podsReadyPct = cluster && cluster.totalPods > 0 ? Math.round((cluster.readyPods / cluster.totalPods) * 100) : null

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
        accent="bg-success/15 text-success"
      />
    </div>
  )
}
