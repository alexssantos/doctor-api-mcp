import { Suspense, lazy, useState } from 'react'

import { Header } from '@/components/dashboard/Header'
import { QuickLinks } from '@/components/dashboard/QuickLinks'
import { StatsCards } from '@/components/dashboard/StatsCards'
import { ApplicationsPanel } from '@/components/dashboard/ApplicationsPanel'
import { TracesPanel } from '@/components/dashboard/TracesPanel'
import { DependenciesPanel } from '@/components/dashboard/DependenciesPanel'
import { ServiceSwitcher } from '@/components/dashboard/ServiceSwitcher'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsContent } from '@/components/ui/tabs'
import { useApplications, useOverview } from '@/lib/api'
import { TABS, type TabId } from '@/lib/tabs'
import { useHashTab } from '@/lib/useHashTab'

// Recharts and the landing page are dead weight on the default tab — Radix
// mounts tab content only when selected, so defer their chunks until then.
const MetricsPanel = lazy(() =>
  import('@/components/dashboard/MetricsPanel').then((m) => ({ default: m.MetricsPanel })),
)
const LandingPage = lazy(() =>
  import('@/components/landing/LandingPage').then((m) => ({ default: m.LandingPage })),
)

function App() {
  const { data: overview, isError: overviewError, dataUpdatedAt, refetch, isFetching } = useOverview()
  const {
    data: applicationsData,
    isLoading: appsLoading,
    isError: appsError,
    refetch: refetchApps,
  } = useApplications()
  const [selectedApp, setSelectedApp] = useState<string | undefined>(undefined)
  const [tab, setTab] = useHashTab<TabId>(TABS, 'operacao')

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
    <Tabs value={tab} onValueChange={setTab} className="min-h-dvh gap-0 bg-background">
      <a href="#main" className="skip-link">
        Pular para o conteúdo
      </a>

      <Header
        lastUpdated={dataUpdatedAt}
        isFetching={isFetching}
        isHealthy={!overviewError && !appsError}
        onRefresh={() => {
          refetch()
          refetchApps()
        }}
      />

      <main id="main" tabIndex={-1} className="mx-auto w-full max-w-7xl px-4 py-5 sm:px-6 lg:px-8">
        <TabsContent value="operacao" className="flex flex-col gap-5">
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
            onRetry={() => refetchApps()}
          />
        </TabsContent>

        <TabsContent value="observabilidade" className="flex flex-col gap-5">
          <ServiceSwitcher
            applications={applications}
            selected={activeApp?.name}
            onSelect={setSelectedApp}
          />

          <div className="grid grid-cols-1 gap-5 xl:grid-cols-2">
            <TracesPanel service={tracesService} />
            <Suspense fallback={<Skeleton className="h-96 w-full" />}>
              <MetricsPanel service={metricsService} />
            </Suspense>
          </div>

          <DependenciesPanel />
        </TabsContent>

        <TabsContent value="projeto">
          <Suspense fallback={<Skeleton className="h-96 w-full" />}>
            <LandingPage
              cluster={overview?.cluster}
              applications={applications}
              enabledCount={enabledCount}
              links={overview?.links}
              onNavigate={setTab}
            />
          </Suspense>
        </TabsContent>
      </main>

      <footer className="mx-auto max-w-7xl px-4 py-8 text-center text-xs text-muted-foreground sm:px-6 lg:px-8">
        mcp-apis · Painel de Observabilidade · dados de Jaeger, Prometheus e Kubernetes
      </footer>
    </Tabs>
  )
}

export default App
