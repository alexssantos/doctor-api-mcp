import { AlertCircle, RadarIcon, RefreshCw } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Card, CardAction, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { ApplicationCard } from '@/components/dashboard/ApplicationCard'
import { useRescanDiscovery, useSetIndexing } from '@/lib/api'
import { cn } from '@/lib/utils'
import type { DiscoveredApplication } from '@/lib/api'

export function ApplicationsPanel({
  applications,
  lastScanAt,
  isLoading,
  isError,
  selectedApp,
  onSelectApp,
}: {
  applications: DiscoveredApplication[]
  lastScanAt: string | null
  isLoading: boolean
  isError: boolean
  selectedApp?: string
  onSelectApp: (name: string) => void
}) {
  const setIndexing = useSetIndexing()
  const rescan = useRescanDiscovery()

  const enabledCount = applications.filter((app) => app.enabled).length

  return (
    <Card>
      <CardHeader>
        <CardTitle className="items-center gap-2">
          <RadarIcon className="size-4 text-muted-foreground" />
          Aplicações descobertas
          {applications.length > 0 && (
            <span className="text-xs font-normal text-muted-foreground">
              {applications.length} descoberta{applications.length > 1 ? 's' : ''} ·{' '}
              {enabledCount} habilitada{enabledCount !== 1 ? 's' : ''} para o MCP
            </span>
          )}
        </CardTitle>
        <CardAction>
          <Button
            variant="outline"
            size="sm"
            onClick={() => rescan.mutate()}
            disabled={rescan.isPending}
            title={lastScanAt ? `Último scan: ${new Date(lastScanAt).toLocaleTimeString()}` : undefined}
          >
            <RefreshCw className={cn('size-3.5', rescan.isPending && 'animate-spin')} />
            Re-scan
          </Button>
        </CardAction>
      </CardHeader>
      <CardContent>
        {isError && (
          <div className="flex items-center gap-2 rounded-lg border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive">
            <AlertCircle className="size-4 shrink-0" />
            Não foi possível carregar as aplicações. Verifique o backend do MCP Server.
          </div>
        )}

        {isLoading && (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-36 w-full" />
            ))}
          </div>
        )}

        {!isLoading && !isError && applications.length === 0 && (
          <p className="py-6 text-center text-sm text-muted-foreground">
            Nenhuma aplicação descoberta ainda. Verifique a configuração de Discovery e o RBAC do cluster.
          </p>
        )}

        {!isLoading && applications.length > 0 && (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {applications.map((app) => (
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
