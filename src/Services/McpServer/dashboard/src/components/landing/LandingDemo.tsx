import { Activity, ExternalLink, Moon, Radar, Sun } from 'lucide-react'

import { LandingPage } from '@/components/landing/LandingPage'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { useDarkMode } from '@/hooks/use-dark-mode'
import { mockApplications, mockCluster, mockEnabledCount, mockLinks } from '@/lib/mock-data'

// Build estático publicado no GitHub Pages: mesma aba Projeto do dashboard,
// mas alimentada por dados simulados porque não há backend no GitHub Pages.
function scrollToTop() {
  window.scrollTo({ top: 0, behavior: 'smooth' })
}

export function LandingDemo() {
  const { isDark, toggle } = useDarkMode()

  return (
    <div className="min-h-dvh bg-background">
      <header className="border-b border-border bg-background/85 backdrop-blur">
        <div className="mx-auto flex max-w-[90rem] items-center justify-between gap-4 px-4 py-3 sm:px-6 lg:px-8">
          <div className="flex items-center gap-3">
            <div className="flex size-9 items-center justify-center rounded-lg bg-primary text-primary-foreground">
              <Activity className="size-5" />
            </div>
            <div className="min-w-0">
              <h1 className="truncate text-base font-semibold leading-tight">mcp-apis</h1>
              <p className="hidden text-xs text-muted-foreground sm:block">
                Inteligência operacional, timeline e RCA
              </p>
            </div>
          </div>

          <div className="flex items-center gap-2">
            <a
              href="https://github.com/alexssantos/doctor-api-mcp"
              target="_blank"
              rel="noreferrer noopener"
              className="inline-flex min-h-11 items-center gap-1.5 rounded-md border border-border px-3 text-xs font-medium text-muted-foreground transition-colors hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
            >
              <ExternalLink className="size-3.5" />
              Ver no GitHub
            </a>

            <Button
              variant="outline"
              size="icon"
              onClick={toggle}
              aria-label={isDark ? 'Mudar para tema claro' : 'Mudar para tema escuro'}
            >
              {isDark ? <Sun className="size-4" /> : <Moon className="size-4" />}
            </Button>
          </div>
        </div>
      </header>

      <main className="mx-auto w-full max-w-[90rem] px-4 py-5 sm:px-6 lg:px-8">
        <div className="mb-5 flex justify-center sm:justify-start">
          <Badge variant="outline">
            <Radar className="size-3" />
            Dados de demonstração — não reflete um cluster ao vivo
          </Badge>
        </div>

        <LandingPage
          cluster={mockCluster}
          applications={mockApplications}
          enabledCount={mockEnabledCount}
          links={mockLinks}
          onNavigate={scrollToTop}
        />
      </main>

      <footer className="mx-auto max-w-[90rem] px-4 py-8 text-center text-xs text-muted-foreground sm:px-6 lg:px-8">
        mcp-apis · Painel de Observabilidade · demonstração com dados simulados
      </footer>
    </div>
  )
}
