import { Boxes, Clock3 } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { applicationKey, type DiscoveredApplication } from '@/lib/api'

/**
 * The observability panels follow the app picked on the Operação tab. Without
 * this control the selection would be invisible — and unchangeable — from here.
 */
export function ServiceSwitcher({
  applications,
  selectedKey,
  onSelect,
  minutes,
  onMinutesChange,
}: {
  applications: DiscoveredApplication[]
  selectedKey?: string
  onSelect: (key: string) => void
  minutes: number
  onMinutesChange: (minutes: number) => void
}) {
  if (applications.length === 0) return null

  const current = applications.find((app) => applicationKey(app) === selectedKey)

  return (
    <Card>
      <CardContent className="grid gap-3 p-4 lg:grid-cols-[1fr_auto_auto] lg:items-center">
        <div className="flex items-center gap-2">
          <Boxes className="size-4 shrink-0 text-muted-foreground" />
          <label htmlFor="service-switcher" className="text-sm font-medium">
            Serviço em análise
          </label>
          {current && !current.enabled && (
            <Badge variant="outline" title="Desabilitada para indexação MCP">
              não indexada
            </Badge>
          )}
        </div>

        <Select value={selectedKey ?? ''} onValueChange={onSelect}>
          <SelectTrigger id="service-switcher" className="w-full font-mono text-xs lg:w-80">
            <SelectValue placeholder="Selecione um serviço" />
          </SelectTrigger>
          <SelectContent>
            {applications.map((app) => (
              <SelectItem key={applicationKey(app)} value={applicationKey(app)} className="font-mono text-xs">
                {app.name}
                {app.namespace ? ` · ${app.namespace}` : ''}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>

        <div className="flex items-center gap-2">
          <Clock3 className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
          <label htmlFor="analysis-window" className="sr-only">Janela de análise</label>
          <Select value={String(minutes)} onValueChange={(value) => onMinutesChange(Number(value))}>
            <SelectTrigger id="analysis-window" className="w-full text-xs lg:w-40" aria-label="Janela de análise">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="15">Últimos 15 min</SelectItem>
              <SelectItem value="30">Últimos 30 min</SelectItem>
              <SelectItem value="60">Última 1 hora</SelectItem>
              <SelectItem value="120">Últimas 2 horas</SelectItem>
              <SelectItem value="360">Últimas 6 horas</SelectItem>
            </SelectContent>
          </Select>
        </div>
      </CardContent>
    </Card>
  )
}
