import { useState } from 'react'
import { Header } from '@/components/dashboard/Header'
import { QuickLinks } from '@/components/dashboard/QuickLinks'
import { StatsCards } from '@/components/dashboard/StatsCards'
import { ServicesGrid } from '@/components/dashboard/ServicesGrid'
import { TracesPanel } from '@/components/dashboard/TracesPanel'
import { DependenciesPanel } from '@/components/dashboard/DependenciesPanel'
import { MetricsPanel } from '@/components/dashboard/MetricsPanel'
import { useOverview } from '@/lib/api'

function App() {
  const { data: overview, isLoading, isError, dataUpdatedAt, refetch, isFetching } = useOverview()
  const [selectedService, setSelectedService] = useState<string | undefined>(undefined)

  const services = overview?.services ?? []
  const activeService = selectedService ?? services[0]?.name

  return (
    <div className="min-h-screen bg-background">
      <Header
        lastUpdated={dataUpdatedAt}
        isFetching={isFetching}
        isHealthy={!isError}
        onRefresh={() => refetch()}
      />

      <main className="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-6 sm:px-6 lg:px-8">
        <QuickLinks links={overview?.links} />

        <StatsCards cluster={overview?.cluster} servicesCount={services.length} isLoading={isLoading} />

        <ServicesGrid
          services={services}
          isLoading={isLoading}
          isError={isError}
          selectedService={activeService}
          onSelectService={setSelectedService}
        />

        <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
          <TracesPanel service={activeService} />
          <MetricsPanel service={activeService} />
        </div>

        <DependenciesPanel />
      </main>

      <footer className="mx-auto max-w-7xl px-4 py-8 text-center text-xs text-muted-foreground sm:px-6 lg:px-8">
        mcp-apis · Painel de Observabilidade · dados de Jaeger, Prometheus e Kubernetes
      </footer>
    </div>
  )
}

export default App

