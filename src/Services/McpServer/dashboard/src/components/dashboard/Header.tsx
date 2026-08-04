import { useEffect, useState } from 'react'
import { Activity, Moon, RefreshCw, Sun } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Badge } from '@/components/ui/badge'
import { cn } from '@/lib/utils'

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
    <header className="sticky top-0 z-10 border-b border-border bg-background/85 backdrop-blur supports-[backdrop-filter]:bg-background/70">
      <div className="mx-auto flex max-w-7xl items-center justify-between gap-4 px-4 py-4 sm:px-6 lg:px-8">
        <div className="flex items-center gap-3">
          <div className="flex size-9 items-center justify-center rounded-lg bg-primary text-primary-foreground">
            <Activity className="size-5" />
          </div>
          <div>
            <h1 className="text-base font-semibold leading-tight">mcp-apis Dashboard</h1>
            <p className="text-xs text-muted-foreground">Observabilidade de serviços, métricas e traces</p>
          </div>
        </div>

        <div className="flex items-center gap-2">
          <Badge variant={isHealthy ? 'success' : 'destructive'} className="hidden sm:inline-flex">
            <span
              className={cn(
                'size-1.5 rounded-full',
                isHealthy ? 'bg-success' : 'bg-destructive',
              )}
            />
            {isHealthy ? 'Operacional' : 'Falha ao coletar dados'}
          </Badge>

          <span className="hidden text-xs text-muted-foreground md:inline">
            Atualizado às {formatTime(lastUpdated)}
          </span>

          <Button variant="outline" size="icon" onClick={onRefresh} aria-label="Atualizar">
            <RefreshCw className={cn('size-4', isFetching && 'animate-spin')} />
          </Button>

          <Button variant="outline" size="icon" onClick={toggle} aria-label="Alternar tema">
            {isDark ? <Sun className="size-4" /> : <Moon className="size-4" />}
          </Button>
        </div>
      </div>
    </header>
  )
}
