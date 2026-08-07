import { Suspense, lazy, useState } from 'react'
import { useIsFetching, useQueryClient } from '@tanstack/react-query'

import { AnomaliesPanel } from '@/components/dashboard/AnomaliesPanel'
import { ApplicationsPanel } from '@/components/dashboard/ApplicationsPanel'
import { DependenciesPanel } from '@/components/dashboard/DependenciesPanel'
import { Header } from '@/components/dashboard/Header'
import { IncidentTimelinePanel } from '@/components/dashboard/IncidentTimelinePanel'
import { QuickLinks } from '@/components/dashboard/QuickLinks'
import { RootCausePanel } from '@/components/dashboard/RootCausePanel'
import { ServiceHealthPanel } from '@/components/dashboard/ServiceHealthPanel'
import { ServiceSpecPanel } from '@/components/dashboard/ServiceSpecPanel'
import { ServiceSwitcher } from '@/components/dashboard/ServiceSwitcher'
import { SystemOverviewPanel } from '@/components/dashboard/SystemOverviewPanel'
import { Skeleton } from '@/components/ui/skeleton'
import { Tabs, TabsContent } from '@/components/ui/tabs'
import { applicationKey, useApplications, useOverview } from '@/lib/api'
import { TABS, type TabId } from '@/lib/tabs'
import { useHashTab } from '@/lib/useHashTab'

const LandingPage = lazy(() =>
  import('@/components/landing/LandingPage').then((module) => ({ default: module.LandingPage })),
)

function App() {
  const overviewQuery = useOverview()
  const applicationsQuery = useApplications()
  const queryClient = useQueryClient()
  const fetchingCount = useIsFetching()
  const [selectedKey, setSelectedKey] = useState<string>()
  const [minutes, setMinutes] = useState(30)
  const [tab, setTab] = useHashTab<TabId>(TABS, 'visao')

  const applications = applicationsQuery.data?.applications ?? []
  const activeApp =
    applications.find((app) => applicationKey(app) === selectedKey) ??
    applications.find((app) => app.enabled && app.namespace) ??
    applications[0]
  const activeKey = activeApp ? applicationKey(activeApp) : undefined
  const enabledCount = applications.filter((app) => app.enabled).length
  const lastUpdated = Math.max(overviewQuery.dataUpdatedAt, applicationsQuery.dataUpdatedAt)

  const inspectService = (namespace: string, name: string) => {
    const app = applications.find((candidate) => candidate.namespace === namespace && candidate.name === name)
    if (app) setSelectedKey(applicationKey(app))
    setTab('servico')
  }

  return (
    <Tabs value={tab} onValueChange={setTab} className="min-h-dvh gap-0 bg-background">
      <a href="#main" className="skip-link">Pular para o conteúdo</a>

      <Header
        lastUpdated={lastUpdated}
        isFetching={fetchingCount > 0}
        systemStatus={overviewQuery.data?.system.healthStatus}
        hasDataError={overviewQuery.isError || applicationsQuery.isError}
        onRefresh={() => queryClient.invalidateQueries()}
      />

      <main id="main" tabIndex={-1} className="mx-auto w-full max-w-[90rem] px-4 py-5 sm:px-6 lg:px-8">
        <TabsContent value="visao" className="flex flex-col gap-4">
          <SystemOverviewPanel
            overview={overviewQuery.data}
            isLoading={overviewQuery.isLoading}
            isError={overviewQuery.isError}
            onRetry={() => overviewQuery.refetch()}
            onInspect={(service) => inspectService(service.namespace, service.serviceName)}
          />
          <QuickLinks links={overviewQuery.data?.links} />
        </TabsContent>

        <TabsContent value="servico" className="flex flex-col gap-4">
          <ServiceSwitcher
            applications={applications}
            selectedKey={activeKey}
            onSelect={setSelectedKey}
            minutes={minutes}
            onMinutesChange={setMinutes}
          />
          <div className="grid gap-4 xl:grid-cols-2">
            <ServiceHealthPanel app={activeApp} minutes={minutes} />
            <AnomaliesPanel app={activeApp} minutes={minutes} />
          </div>
          <DependenciesPanel app={activeApp} minutes={minutes} />
          <ServiceSpecPanel app={activeApp} />
        </TabsContent>

        <TabsContent value="incidente" className="flex flex-col gap-4">
          <ServiceSwitcher
            applications={applications}
            selectedKey={activeKey}
            onSelect={setSelectedKey}
            minutes={minutes}
            onMinutesChange={setMinutes}
          />
          <div className="grid items-start gap-4 xl:grid-cols-2">
            <IncidentTimelinePanel app={activeApp} minutes={minutes} />
            <RootCausePanel app={activeApp} minutes={minutes} />
          </div>
        </TabsContent>

        <TabsContent value="catalogo">
          <ApplicationsPanel
            applications={applications}
            lastScanAt={applicationsQuery.data?.lastScanAt ?? null}
            isLoading={applicationsQuery.isLoading}
            isError={applicationsQuery.isError}
            canManage={applicationsQuery.data?.canManage ?? false}
            selectedAppKey={activeKey}
            onSelectApp={(key) => {
              setSelectedKey(key)
              setTab('servico')
            }}
            onRetry={() => applicationsQuery.refetch()}
          />
        </TabsContent>

        <TabsContent value="projeto">
          <Suspense fallback={<Skeleton className="h-96 w-full" />}>
            <LandingPage
              cluster={overviewQuery.data?.cluster}
              applications={applications}
              enabledCount={enabledCount}
              links={overviewQuery.data?.links}
              onNavigate={setTab}
            />
          </Suspense>
        </TabsContent>
      </main>

      <footer className="mx-auto max-w-[90rem] px-4 py-8 text-center text-xs text-muted-foreground sm:px-6 lg:px-8">
        mcp-apis · Health, dependências, anomalias, timeline e RCA · Prometheus, Jaeger, Loki e Kubernetes
      </footer>
    </Tabs>
  )
}

export default App
