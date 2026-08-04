import { AlertCircle } from 'lucide-react'

import { Card, CardContent, CardHeader, CardTitle } from '@/components/ui/card'
import { Skeleton } from '@/components/ui/skeleton'
import { ServiceCard } from '@/components/dashboard/ServiceCard'
import type { ServiceOverview } from '@/lib/api'

export function ServicesGrid({
  services,
  isLoading,
  isError,
  selectedService,
  onSelectService,
}: {
  services: ServiceOverview[]
  isLoading: boolean
  isError: boolean
  selectedService?: string
  onSelectService: (name: string) => void
}) {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Serviços conectados</CardTitle>
      </CardHeader>
      <CardContent>
        {isError && (
          <div className="flex items-center gap-2 rounded-lg border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive">
            <AlertCircle className="size-4 shrink-0" />
            Não foi possível carregar os serviços. Verifique o backend do MCP Server.
          </div>
        )}

        {isLoading && (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-32 w-full" />
            ))}
          </div>
        )}

        {!isLoading && !isError && services.length === 0 && (
          <p className="py-6 text-center text-sm text-muted-foreground">
            Nenhum serviço indexado ainda. Verifique a configuração de Discovery.
          </p>
        )}

        {!isLoading && services.length > 0 && (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {services.map((service) => (
              <ServiceCard
                key={service.name}
                service={service}
                selected={service.name === selectedService}
                onSelect={() => onSelectService(service.name)}
              />
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  )
}
