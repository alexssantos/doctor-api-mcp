import { Boxes } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import type { DiscoveredApplication } from '@/lib/api'

/**
 * The observability panels follow the app picked on the Operação tab. Without
 * this control the selection would be invisible — and unchangeable — from here.
 */
export function ServiceSwitcher({
  applications,
  selected,
  onSelect,
}: {
  applications: DiscoveredApplication[]
  selected?: string
  onSelect: (name: string) => void
}) {
  if (applications.length === 0) return null

  const current = applications.find((app) => app.name === selected)

  return (
    <Card>
      <CardContent className="flex flex-col gap-3 p-4 sm:flex-row sm:items-center sm:justify-between">
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

        <Select value={selected ?? ''} onValueChange={onSelect}>
          <SelectTrigger id="service-switcher" className="font-mono text-xs sm:w-72">
            <SelectValue placeholder="Selecione um serviço" />
          </SelectTrigger>
          <SelectContent>
            {applications.map((app) => (
              <SelectItem key={app.name} value={app.name} className="font-mono text-xs">
                {app.name}
                {app.namespace ? ` · ${app.namespace}` : ''}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </CardContent>
    </Card>
  )
}
