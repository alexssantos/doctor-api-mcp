import { Activity, Boxes, BrainCircuit, LayoutDashboard, Microscope, Moon, RefreshCw, Sparkles, Sun } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useDarkMode } from '@/hooks/use-dark-mode'
import { TABS, type TabId } from '@/lib/tabs'
import { cn } from '@/lib/utils'
import type { HealthState } from '@/lib/api'

const TAB_META: Record<TabId, { label: string; icon: typeof Activity }> = {
  visao: { label: 'Visão geral', icon: LayoutDashboard },
  servico: { label: 'Serviço', icon: Microscope },
  incidente: { label: 'Incidente', icon: BrainCircuit },
  catalogo: { label: 'Catálogo', icon: Boxes },
  projeto: { label: 'Projeto', icon: Sparkles },
}

function formatTime(ts?: number) {
  if (!ts) return '—'
  return new Date(ts).toLocaleTimeString('pt-BR', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
}

export function Header({
  lastUpdated,
  isFetching,
  systemStatus,
  hasDataError,
  onRefresh,
}: {
  lastUpdated?: number
  isFetching: boolean
  systemStatus?: HealthState
  hasDataError: boolean
  onRefresh: () => void
}) {
  const { isDark, toggle } = useDarkMode()
  const statusMeta = hasDataError
    ? { label: 'Dados indisponíveis', variant: 'destructive' as const, dot: 'bg-destructive' }
    : systemStatus == null
      ? { label: 'Coletando dados', variant: 'outline' as const, dot: 'bg-muted-foreground' }
      : systemStatus === 'critical'
      ? { label: 'Sistema crítico', variant: 'destructive' as const, dot: 'bg-destructive' }
      : systemStatus === 'degraded'
        ? { label: 'Sistema degradado', variant: 'warning' as const, dot: 'bg-warning' }
        : systemStatus === 'unknown'
          ? { label: 'Cobertura desconhecida', variant: 'outline' as const, dot: 'bg-muted-foreground' }
          : { label: 'Sistema operacional', variant: 'success' as const, dot: 'bg-success' }

  return (
    <header className="sticky top-0 z-[var(--z-sticky)] border-b border-border bg-background/85 backdrop-blur supports-[backdrop-filter]:bg-background/70">
      <div className="mx-auto flex max-w-[90rem] items-center justify-between gap-4 px-4 pt-3 sm:px-6 lg:px-8">
        <div className="flex items-center gap-3">
          <div className="flex size-9 items-center justify-center rounded-lg bg-primary text-primary-foreground">
            <Activity className="size-5" />
          </div>
          <div className="min-w-0">
            <h1 className="truncate text-base font-semibold leading-tight">mcp-apis</h1>
            <p className="hidden text-xs text-muted-foreground sm:block">
              Inteligência operacional orientada por evidências
            </p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <Badge variant={statusMeta.variant} className="hidden sm:inline-flex">
            <span className={cn('size-1.5 rounded-full', statusMeta.dot)} />
            {statusMeta.label}
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
      <nav className="mx-auto max-w-[90rem] px-4 pb-2 pt-2 sm:px-6 lg:px-8" aria-label="Seções do dashboard">
        <TabsList className="w-full justify-start overflow-x-auto scrollbar-thin sm:w-auto">
          {TABS.map((id) => {
            const { label, icon: Icon } = TAB_META[id]
            return (
              // Equal widths while the bar is full-width on mobile; natural
              // widths once it shrinks to fit, where flex-basis:0 would make
              // the browser resolve trigger widths inconsistently.
              <TabsTrigger key={id} value={id} className="flex-none">
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
