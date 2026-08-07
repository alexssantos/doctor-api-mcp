import {
  Activity,
  AlertTriangle,
  Boxes,
  CheckCircle2,
  EyeOff,
  FileJson,
  Lock,
  Network,
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
  if (!health || health.healthStatus === 'unknown') {
    return { label: 'Desconhecido', variant: 'outline' as const, icon: AlertTriangle }
  }
  if (health.healthStatus === 'healthy') {
    return { label: 'Saudável', variant: 'success' as const, icon: CheckCircle2 }
  }
  if (health.healthStatus === 'critical') {
    return { label: 'Crítico', variant: 'destructive' as const, icon: XCircle }
  }
  return { label: 'Degradado', variant: 'warning' as const, icon: AlertTriangle }
}

function minutesSince(iso: string) {
  return Math.max(1, Math.round((Date.now() - new Date(iso).getTime()) / 60_000))
}

export function ApplicationCard({
  app,
  canManage,
  selected,
  onSelect,
  onToggle,
  isToggling,
}: {
  app: DiscoveredApplication
  canManage: boolean
  selected: boolean
  onSelect: () => void
  onToggle: (enabled: boolean) => void
  isToggling: boolean
}) {
  const status = healthStatus(app)
  const StatusIcon = status.icon
  const score = app.health?.score

  return (
    // The card itself is a plain container. Making it a `role="button"` while it
    // contains a Switch nests interactive controls, which hides the switch from
    // assistive tech — so selection lives on the real <button> below instead.
    <Card
      className={cn(
        'transition-colors focus-within:border-primary/60',
        selected ? 'border-primary ring-1 ring-ring' : 'hover:border-primary/40',
        !app.enabled && 'opacity-70',
      )}
    >
      <CardHeader>
        <CardTitle className="justify-between gap-2">
          <button
            type="button"
            onClick={onSelect}
            aria-pressed={selected}
            title={`Abrir inteligência de ${app.name}`}
            className="inline-flex min-h-11 min-w-0 cursor-pointer items-center truncate rounded font-mono text-sm hover:text-primary focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
          >
            {app.name}
          </button>

          <span className="flex shrink-0 items-center gap-2">
            <Badge variant={status.variant}>
              <StatusIcon className="size-3" />
              {status.label}
            </Badge>
            {app.lockedDisabled || !canManage ? (
              <Tooltip>
                <TooltipTrigger asChild>
                  <span className="inline-flex">
                    <Switch
                      checked={app.enabled}
                      disabled
                      aria-label={
                        app.lockedDisabled
                          ? `Indexação de ${app.name} travada`
                          : `Indexação de ${app.name} disponível apenas para administradores`
                      }
                    />
                  </span>
                </TooltipTrigger>
                <TooltipContent>
                  {app.lockedDisabled
                    ? 'Travado pela label mcp-apis/indexed=false no Service. Remova a label para liberar.'
                    : 'Permissão administrativa necessária para alterar a indexação.'}
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

        {(app.version || app.team || app.owner) && (
          <p className="truncate text-[11px] text-muted-foreground">
            {app.version ? `v${app.version}` : app.revision ? `rev. ${app.revision}` : ''}
            {(app.version || app.revision) && (app.team || app.owner) ? ' · ' : ''}
            {app.team ?? app.owner ?? ''}
          </p>
        )}

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
          {app.desiredReplicas > 0 && (
            <Badge variant="outline" className="tabular">
              {app.readyReplicas}/{app.desiredReplicas} réplicas
            </Badge>
          )}
          {score != null && (
            <Badge variant="outline" className="tabular">
              score {score.toFixed(0)} · cobertura {Math.round((app.health?.coverage ?? 0) * 100)}%
            </Badge>
          )}
          {app.missing && (
            <Badge variant="destructive" className="tabular">
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
