import { ArrowRight, Waypoints } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Reveal } from '@/components/ui/reveal'
import { Hero } from '@/components/landing/Hero'
import { InstallSection } from '@/components/landing/InstallSection'
import { SignalsSection } from '@/components/landing/SignalsSection'
import { ToolsGrid } from '@/components/landing/ToolsGrid'
import { ChatDemoSection } from '@/components/landing/ChatDemoSection'
import { ArchitectureFlow } from '@/components/landing/ArchitectureFlow'
import { ConnectSection } from '@/components/landing/ConnectSection'
import { StackSection } from '@/components/landing/StackSection'
import type { ClusterSummary, DashboardLinks, DiscoveredApplication } from '@/lib/api'

/**
 * The "Projeto" tab: a product page for the MCP server built around the radar
 * metaphor — discovery first, then the signals it correlates for the agent.
 * The hero numbers come from the same live queries the operational tabs use.
 */
export function LandingPage({
  cluster,
  applications,
  enabledCount,
  links,
  onNavigate,
}: {
  cluster?: ClusterSummary
  applications: DiscoveredApplication[]
  enabledCount: number
  links?: DashboardLinks
  onNavigate: (tab: string) => void
}) {
  return (
    <div className="flex flex-col gap-12 pb-4 sm:gap-16">
      <Hero
        cluster={cluster}
        applications={applications}
        enabledCount={enabledCount}
        onNavigate={onNavigate}
      />

      <InstallSection />

      <SignalsSection />

      <ToolsGrid />

      <ChatDemoSection />

      <ArchitectureFlow />

      <ConnectSection />

      <StackSection links={links} />

      {/* Closing CTA — sends the reader into the live data instead of off-site. */}
      <Reveal>
        <section className="rounded-2xl border border-border bg-gradient-to-br from-primary/10 via-card to-chart-2/10 px-6 py-10 text-center sm:px-10">
          <h3 className="text-balance text-xl font-semibold tracking-tight sm:text-2xl">
            Tudo o que o agente enxerga está a uma aba daqui
          </h3>
          <p className="mx-auto mt-2 max-w-xl text-pretty text-sm text-muted-foreground">
            Os mesmos engines e caches que respondem às tools MCP alimentam saúde, anomalias,
            dependências, timeline e RCA neste dashboard.
          </p>
          <div className="mt-5 flex flex-wrap justify-center gap-3">
            <Button onClick={() => onNavigate('catalogo')}>
              Abrir catálogo
              <ArrowRight className="size-4" />
            </Button>
            <Button variant="outline" onClick={() => onNavigate('servico')}>
              <Waypoints className="size-4" />
              Analisar serviço
            </Button>
          </div>
        </section>
      </Reveal>
    </div>
  )
}
