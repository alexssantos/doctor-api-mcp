import { AlertTriangle, CheckCircle2, FileJson, RotateCcw, XCircle } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { cn } from '@/lib/utils'
import type { ServiceOverview } from '@/lib/api'

function healthStatus(service: ServiceOverview) {
  const health = service.health
  if (!health || health.podCount === 0) {
    return { label: 'Sem pods', variant: 'outline' as const, icon: AlertTriangle }
  }
  if (health.allReady) {
    return { label: 'Saudável', variant: 'success' as const, icon: CheckCircle2 }
  }
  return { label: 'Degradado', variant: 'warning' as const, icon: XCircle }
}

export function ServiceCard({
  service,
  selected,
  onSelect,
}: {
  service: ServiceOverview
  selected: boolean
  onSelect: () => void
}) {
  const status = healthStatus(service)
  const StatusIcon = status.icon
  const restarts = service.health?.pods.reduce((sum, p) => sum + p.restarts, 0) ?? 0

  return (
    <Card
      role="button"
      tabIndex={0}
      onClick={onSelect}
      onKeyDown={(e) => e.key === 'Enter' && onSelect()}
      className={cn(
        'cursor-pointer transition-all hover:border-primary/50 hover:shadow-md',
        selected && 'border-primary ring-2 ring-ring',
      )}
    >
      <CardHeader>
        <CardTitle className="justify-between">
          <span className="truncate font-mono text-sm">{service.name}</span>
          <Badge variant={status.variant}>
            <StatusIcon className="size-3" />
            {status.label}
          </Badge>
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-2">
        <p className="truncate text-xs text-muted-foreground" title={service.baseUrl}>
          {service.baseUrl}
        </p>

        <div className="flex flex-wrap items-center gap-2 text-xs">
          <Badge variant="outline">
            <FileJson className="size-3" />
            {service.openApiPath || 'sem spec'}
          </Badge>
          {service.health && (
            <Badge variant="outline">{service.health.podCount} pod(s)</Badge>
          )}
          {restarts > 0 && (
            <Badge variant="warning">
              <RotateCcw className="size-3" />
              {restarts} restart{restarts > 1 ? 's' : ''}
            </Badge>
          )}
        </div>
      </CardContent>
    </Card>
  )
}
