import {
  Activity,
  AlertTriangle,
  Boxes,
  CheckCircle2,
  EyeOff,
  FileJson,
  Lock,
  Network,
  RotateCcw,
  Settings2,
  XCircle,
} from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Switch } from '@/components/ui/switch'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { cn } from '@/lib/utils'
import type { DiscoveredApplication, DiscoverySource } from '@/lib/api'

const SOURCE_META: Record<DiscoverySource, { label: string; icon: typeof Boxes }> = {
  deployment: { label: 'Deploy', icon: Boxes },
  network: { label: 'Rede', icon: Network },
  otel: { label: 'OTel', icon: Activity },
  config: { label: 'Config', icon: Settings2 },
}

function healthStatus(app: DiscoveredApplication) {
  const health = app.health
  if (!health || health.podCount === 0) {
    return { label: 'Sem pods', variant: 'outline' as const, icon: AlertTriangle }
  }
  if (health.allReady) {
    return { label: 'Saudável', variant: 'success' as const, icon: CheckCircle2 }
  }
  return { label: 'Degradado', variant: 'warning' as const, icon: XCircle }
}

function minutesSince(iso: string) {
  return Math.max(1, Math.round((Date.now() - new Date(iso).getTime()) / 60_000))
}

export function ApplicationCard({
  app,
  selected,
  onSelect,
  onToggle,
  isToggling,
}: {
  app: DiscoveredApplication
  selected: boolean
  onSelect: () => void
  onToggle: (enabled: boolean) => void
  isToggling: boolean
}) {
  const status = healthStatus(app)
  const StatusIcon = status.icon
  const restarts = app.health?.pods.reduce((sum, p) => sum + p.restarts, 0) ?? 0

  return (
    <Card
      role="button"
      tabIndex={0}
      onClick={onSelect}
      onKeyDown={(e) => e.key === 'Enter' && onSelect()}
      className={cn(
        'cursor-pointer transition-all hover:border-primary/50 hover:shadow-md',
        selected && 'border-primary ring-2 ring-ring',
        !app.enabled && 'opacity-60',
      )}
    >
      <CardHeader>
        <CardTitle className="justify-between gap-2">
          <span className="min-w-0 truncate font-mono text-sm">{app.name}</span>
          <span
            className="flex shrink-0 items-center gap-2"
            onClick={(e) => e.stopPropagation()}
            onKeyDown={(e) => e.stopPropagation()}
          >
            <Badge variant={status.variant}>
              <StatusIcon className="size-3" />
              {status.label}
            </Badge>
            {app.lockedDisabled ? (
              <Tooltip>
                <TooltipTrigger asChild>
                  <span className="inline-flex">
                    <Switch checked={false} disabled aria-label="Indexação travada" />
                  </span>
                </TooltipTrigger>
                <TooltipContent>
                  Travado pela label mcp-apis/indexed=false no Service. Remova a label para liberar.
                </TooltipContent>
              </Tooltip>
            ) : (
              <Switch
                checked={app.enabled}
                disabled={isToggling}
                onCheckedChange={onToggle}
                aria-label={`Indexação MCP de ${app.name}`}
              />
            )}
          </span>
        </CardTitle>
      </CardHeader>
      <CardContent className="space-y-2">
        <p className="truncate text-xs text-muted-foreground" title={app.baseUrl ?? undefined}>
          {app.baseUrl ?? 'sem endpoint HTTP (detectada apenas por traces)'}
        </p>

        <div className="flex flex-wrap items-center gap-2 text-xs">
          {app.sources.map((source) => {
            const meta = SOURCE_META[source]
            if (!meta) return null
            const SourceIcon = meta.icon
            return (
              <Badge key={source} variant="secondary">
                <SourceIcon className="size-3" />
                {meta.label}
              </Badge>
            )
          })}
          {app.namespace && <Badge variant="outline">{app.namespace}</Badge>}
        </div>

        <div className="flex flex-wrap items-center gap-2 text-xs">
          {app.openApi.validated ? (
            <Badge variant="success">
              <FileJson className="size-3" />
              {app.openApi.path}
            </Badge>
          ) : app.baseUrl ? (
            <Tooltip>
              <TooltipTrigger asChild>
                <span className="inline-flex">
                  <Badge variant="warning">
                    <FileJson className="size-3" />
                    Não indexável
                  </Badge>
                </span>
              </TooltipTrigger>
              <TooltipContent className="max-w-xs">
                {app.openApi.failures.length > 0
                  ? app.openApi.failures.join(' · ')
                  : 'Spec OpenAPI não validada ainda.'}
              </TooltipContent>
            </Tooltip>
          ) : null}
          {app.health && app.health.podCount > 0 && (
            <Badge variant="outline">{app.health.podCount} pod(s)</Badge>
          )}
          {restarts > 0 && (
            <Badge variant="warning">
              <RotateCcw className="size-3" />
              {restarts} restart{restarts > 1 ? 's' : ''}
            </Badge>
          )}
          {app.missing && (
            <Badge variant="destructive">
              <EyeOff className="size-3" />
              Não vista há {minutesSince(app.lastSeen)}min
            </Badge>
          )}
          {app.lockedDisabled && (
            <Badge variant="outline">
              <Lock className="size-3" />
              Travada
            </Badge>
          )}
        </div>
      </CardContent>
    </Card>
  )
}
