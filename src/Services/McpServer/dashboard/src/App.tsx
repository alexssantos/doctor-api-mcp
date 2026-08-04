import { useState } from 'react'
import { Header } from '@/components/dashboard/Header'
import { QuickLinks } from '@/components/dashboard/QuickLinks'
import { StatsCards } from '@/components/dashboard/StatsCards'
import { ApplicationsPanel } from '@/components/dashboard/ApplicationsPanel'
import { TracesPanel } from '@/components/dashboard/TracesPanel'
import { DependenciesPanel } from '@/components/dashboard/DependenciesPanel'
import { MetricsPanel } from '@/components/dashboard/MetricsPanel'
import { useApplications, useOverview } from '@/lib/api'

function App() {
  const { data: overview, isError: overviewError, dataUpdatedAt, refetch, isFetching } = useOverview()
  const {
    data: applicationsData,
    isLoading: appsLoading,
    isError: appsError,
    refetch: refetchApps,
  } = useApplications()
  const [selectedApp, setSelectedApp] = useState<string | undefined>(undefined)

  const applications = applicationsData?.applications ?? []
  const enabledCount = applications.filter((app) => app.enabled).length
  const activeApp =
    applications.find((app) => app.name === selectedApp) ??
    applications.find((app) => app.enabled) ??
    applications[0]

  // Jaeger service names are case-sensitive; prefer the raw OTel name.
  const tracesService = activeApp ? (activeApp.otelServiceName ?? activeApp.name) : undefined
  const metricsService = activeApp?.name

  return (
    <div className="min-h-screen bg-background">
      <Header
        lastUpdated={dataUpdatedAt}
        isFetching={isFetching}
        isHealthy={!overviewError && !appsError}
        onRefresh={() => {
          refetch()
          refetchApps()
        }}
      />

      <main className="mx-auto flex max-w-7xl flex-col gap-6 px-4 py-6 sm:px-6 lg:px-8">
        <QuickLinks links={overview?.links} />

        <StatsCards
          cluster={overview?.cluster}
          applicationsCount={applications.length}
          enabledCount={enabledCount}
          isLoading={appsLoading}
        />

        <ApplicationsPanel
          applications={applications}
          lastScanAt={applicationsData?.lastScanAt ?? null}
          isLoading={appsLoading}
          isError={appsError}
          selectedApp={activeApp?.name}
          onSelectApp={setSelectedApp}
        />

        <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
          <TracesPanel service={tracesService} />
          <MetricsPanel service={metricsService} />
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
