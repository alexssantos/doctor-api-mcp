import { useEffect, useState } from 'react'
import { Activity, LayoutGrid, Moon, RefreshCw, Sparkles, Sun, Waypoints } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { TabsList, TabsTrigger } from '@/components/ui/tabs'
import { TABS, type TabId } from '@/lib/tabs'
import { cn } from '@/lib/utils'

const TAB_META: Record<TabId, { label: string; icon: typeof Activity }> = {
  operacao: { label: 'Operação', icon: LayoutGrid },
  observabilidade: { label: 'Observabilidade', icon: Waypoints },
  projeto: { label: 'Projeto', icon: Sparkles },
}

function useDarkMode() {
  const [isDark, setIsDark] = useState<boolean>(() => {
    if (typeof window === 'undefined') return false
    const stored = localStorage.getItem('mcp-dashboard-theme')
    if (stored) return stored === 'dark'
    return window.matchMedia('(prefers-color-scheme: dark)').matches
  })

  useEffect(() => {
    document.documentElement.classList.toggle('dark', isDark)
    localStorage.setItem('mcp-dashboard-theme', isDark ? 'dark' : 'light')
  }, [isDark])

  return { isDark, toggle: () => setIsDark((v) => !v) }
}

function formatTime(ts?: number) {
  if (!ts) return '—'
  return new Date(ts).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

export function Header({
  lastUpdated,
  isFetching,
  isHealthy,
  onRefresh,
}: {
  lastUpdated?: number
  isFetching: boolean
  isHealthy: boolean
  onRefresh: () => void
}) {
  const { isDark, toggle } = useDarkMode()

  return (
    <header className="sticky top-0 z-[var(--z-sticky)] border-b border-border bg-background/85 backdrop-blur supports-[backdrop-filter]:bg-background/70">
      <div className="mx-auto flex max-w-7xl items-center justify-between gap-4 px-4 pt-3 sm:px-6 lg:px-8">
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

        <div className="flex items-center gap-2">
          <Badge variant={isHealthy ? 'success' : 'destructive'} className="hidden sm:inline-flex">
            <span className={cn('size-1.5 rounded-full', isHealthy ? 'bg-success' : 'bg-destructive')} />
            {isHealthy ? 'Operacional' : 'Falha ao coletar dados'}
          </Badge>

          {/* Announced politely so screen readers hear the 15s refresh without interruption. */}
          <span className="hidden text-xs text-muted-foreground tabular md:inline" aria-live="polite">
            Atualizado às {formatTime(lastUpdated)}
          </span>

          <Button variant="outline" size="icon" onClick={onRefresh} aria-label="Atualizar dados">
            <RefreshCw className={cn('size-4', isFetching && 'animate-spin')} />
          </Button>

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

      {/* Primary navigation stays pinned so it is reachable from any scroll depth. */}
      <nav className="mx-auto max-w-7xl px-4 pb-2 pt-2 sm:px-6 lg:px-8" aria-label="Seções do dashboard">
        <TabsList className="w-full sm:w-auto">
          {TABS.map((id) => {
            const { label, icon: Icon } = TAB_META[id]
            return (
              // Equal widths while the bar is full-width on mobile; natural
              // widths once it shrinks to fit, where flex-basis:0 would make
              // the browser resolve trigger widths inconsistently.
              <TabsTrigger key={id} value={id} className="flex-1 sm:flex-none">
                <Icon aria-hidden="true" />
                {label}
              </TabsTrigger>
            )
          })}
        </TabsList>
      </nav>
    </header>
  )
}
