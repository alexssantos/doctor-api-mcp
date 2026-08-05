import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { Activity, ExternalLink, Radar } from 'lucide-react'

import '@/index.css'
import { Badge } from '@/components/ui/badge'
import { LandingPage } from '@/components/landing/LandingPage'
import { mockApplications, mockCluster, mockEnabledCount, mockLinks } from '@/lib/mock-data'

// Build estático publicado no GitHub Pages: mesma "Projeto" tab do dashboard,
// mas alimentada por dados mockados em vez do backend (que não existe aqui).
// onNavigate normalmente troca de aba no dashboard real; aqui não há outra
// aba, então rola até o topo como fallback razoável.
function scrollToTop() {
  window.scrollTo({ top: 0, behavior: 'smooth' })
}

function LandingDemo() {
  return (
    <div className="min-h-dvh bg-background">
      <header className="border-b border-border bg-background/85 backdrop-blur">
        <div className="mx-auto flex max-w-7xl items-center justify-between gap-4 px-4 py-3 sm:px-6 lg:px-8">
          <div className="flex items-center gap-3">
            <div className="flex size-9 items-center justify-center rounded-lg bg-primary text-primary-foreground">
              <Activity className="size-5" />
            </div>
            <div className="min-w-0">
              <h1 className="truncate text-base font-semibold leading-tight">mcp-apis</h1>
              <p className="hidden text-xs text-muted-foreground sm:block">
                Observabilidade de serviços, métricas e traces
              </p>
            </div>
          </div>

          <a
            href="https://github.com/alexssantos/doctor-api-mcp"
            target="_blank"
            rel="noreferrer noopener"
            className="inline-flex items-center gap-1.5 rounded-md border border-border px-3 py-1.5 text-xs font-medium text-muted-foreground transition-colors hover:text-foreground"
          >
            <ExternalLink className="size-3.5" />
            Ver no GitHub
          </a>
        </div>
      </header>

      <main className="mx-auto w-full max-w-7xl px-4 py-5 sm:px-6 lg:px-8">
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

      <footer className="mx-auto max-w-7xl px-4 py-8 text-center text-xs text-muted-foreground sm:px-6 lg:px-8">
        mcp-apis · Painel de Observabilidade · demonstração com dados mockados
      </footer>
    </div>
  )
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <LandingDemo />
  </StrictMode>,
)
