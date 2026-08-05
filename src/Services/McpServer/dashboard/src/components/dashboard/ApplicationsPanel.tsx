import { useMemo, useState } from 'react'
import { AlertCircle, RadarIcon, RefreshCw, SearchX, Telescope } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { EmptyState } from '@/components/ui/empty-state'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { ApplicationCard } from '@/components/dashboard/ApplicationCard'
import { useRescanDiscovery, useSetIndexing } from '@/lib/api'
import { cn } from '@/lib/utils'
import type { DiscoveredApplication } from '@/lib/api'

type StatusFilter = 'todas' | 'habilitadas' | 'desabilitadas' | 'degradadas'

function matchesStatus(app: DiscoveredApplication, filter: StatusFilter) {
  switch (filter) {
    case 'habilitadas':
      return app.enabled
    case 'desabilitadas':
      return !app.enabled
    case 'degradadas':
      return !app.health || app.health.podCount === 0 || !app.health.allReady
    default:
      return true
  }
}

export function ApplicationsPanel({
  applications,
  lastScanAt,
  isLoading,
  isError,
  selectedApp,
  onSelectApp,
  onRetry,
}: {
  applications: DiscoveredApplication[]
  lastScanAt: string | null
  isLoading: boolean
  isError: boolean
  selectedApp?: string
  onSelectApp: (name: string) => void
  onRetry: () => void
}) {
  const setIndexing = useSetIndexing()
  const rescan = useRescanDiscovery()
  const [search, setSearch] = useState('')
  const [status, setStatus] = useState<StatusFilter>('todas')

  const enabledCount = applications.filter((app) => app.enabled).length

  const visible = useMemo(() => {
    const term = search.trim().toLowerCase()
    return applications.filter(
      (app) =>
        matchesStatus(app, status) &&
        (term === '' ||
          app.name.toLowerCase().includes(term) ||
          (app.namespace ?? '').toLowerCase().includes(term) ||
          (app.baseUrl ?? '').toLowerCase().includes(term)),
    )
  }, [applications, search, status])

  // Filters only earn their space once the list is long enough to scan.
  const showFilters = !isLoading && !isError && applications.length > 3

  return (
    <Card aria-busy={isLoading}>
      <CardHeader>
        <CardTitle className="items-center gap-2">
          <RadarIcon className="size-4 text-muted-foreground" />
          Aplicações descobertas
          {applications.length > 0 && (
            <span className="text-xs font-normal text-muted-foreground tabular">
              {applications.length} descoberta{applications.length > 1 ? 's' : ''} · {enabledCount} habilitada
              {enabledCount !== 1 ? 's' : ''} para o MCP
            </span>
          )}
        </CardTitle>
        <CardAction>
          <Button
            variant="outline"
            size="sm"
            onClick={() => rescan.mutate()}
            disabled={rescan.isPending}
            title={lastScanAt ? `Último scan: ${new Date(lastScanAt).toLocaleTimeString('pt-BR')}` : undefined}
          >
            <RefreshCw className={cn('size-3.5', rescan.isPending && 'animate-spin')} />
            Re-scan
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent className="space-y-3">
        {showFilters && (
          <div className="flex flex-col gap-2 sm:flex-row">
            <Input
              type="search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Filtrar por nome, namespace ou URL"
              aria-label="Filtrar aplicações"
              className="sm:max-w-xs"
            />
            <Select value={status} onValueChange={(value) => setStatus(value as StatusFilter)}>
              <SelectTrigger aria-label="Filtrar por status" className="sm:w-48">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectItem value="todas">Todas</SelectItem>
                <SelectItem value="habilitadas">Habilitadas no MCP</SelectItem>
                <SelectItem value="desabilitadas">Desabilitadas</SelectItem>
                <SelectItem value="degradadas">Degradadas</SelectItem>
              </SelectContent>
            </Select>
          </div>
        )}

        {isError && (
          <EmptyState
            variant="error"
            icon={AlertCircle}
            title="Não foi possível carregar as aplicações"
            description="Verifique se o backend do MCP Server está no ar e se o RBAC do cluster permite listar deployments."
            action={{ label: 'Tentar novamente', onClick: onRetry }}
          />
        )}

        {isLoading && (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-36 w-full" />
            ))}
          </div>
        )}

        {!isLoading && !isError && applications.length === 0 && (
          <EmptyState
            icon={Telescope}
            title="Nenhuma aplicação descoberta ainda"
            description="A descoberta varre deployments, rede e traces OTel. Confirme a configuração de Discovery e o RBAC do cluster, ou force uma nova varredura."
            action={{ label: 'Rodar re-scan', onClick: () => rescan.mutate(), isPending: rescan.isPending }}
          />
        )}

        {!isLoading && !isError && applications.length > 0 && visible.length === 0 && (
          <EmptyState
            icon={SearchX}
            title="Nenhuma aplicação corresponde ao filtro"
            description="Ajuste a busca ou volte para “Todas”."
            action={{
              label: 'Limpar filtros',
              onClick: () => {
                setSearch('')
                setStatus('todas')
              },
            }}
          />
        )}

        {!isLoading && visible.length > 0 && (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {visible.map((app) => (
              <ApplicationCard
                key={app.name}
                app={app}
                selected={app.name === selectedApp}
                onSelect={() => onSelectApp(app.name)}
                onToggle={(enabled) => setIndexing.mutate({ name: app.name, enabled })}
                isToggling={setIndexing.isPending && setIndexing.variables?.name === app.name}
              />
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  )
}
