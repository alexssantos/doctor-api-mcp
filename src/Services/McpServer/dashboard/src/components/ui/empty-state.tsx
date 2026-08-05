import { AlertTriangle } from 'lucide-react'
import type { ComponentType, ReactNode } from 'react'

import { Button } from '@/components/ui/button'
import { cn } from '@/lib/utils'

/**
 * Shared placeholder for "nothing here" and "it broke" states. Both get an
 * icon, an explanation and — when recovery is possible — an action, instead of
 * a bare line of muted text.
 */
export function EmptyState({
  icon: Icon,
  title,
  description,
  action,
  variant = 'empty',
  className,
}: {
  icon: ComponentType<{ className?: string }>
  title: string
  description?: ReactNode
  action?: { label: string; onClick: () => void; isPending?: boolean }
  variant?: 'empty' | 'error'
  className?: string
}) {
  const isError = variant === 'error'

  return (
    <div
      role={isError ? 'alert' : undefined}
      className={cn(
        'flex flex-col items-center justify-center gap-2 rounded-lg border border-dashed px-4 py-8 text-center',
        isError ? 'border-destructive/30 bg-destructive/5' : 'border-border bg-muted/30',
        className,
      )}
    >
      <div
        className={cn(
          'flex size-9 items-center justify-center rounded-full',
          isError ? 'bg-destructive/10 text-destructive' : 'bg-muted text-muted-foreground',
        )}
      >
        <Icon className="size-4.5" />
      </div>

      <p className={cn('text-sm font-medium', isError && 'text-destructive')}>{title}</p>

      {description && <p className="max-w-sm text-xs text-muted-foreground">{description}</p>}

      {action && (
        <Button variant="outline" size="sm" className="mt-1" onClick={action.onClick} disabled={action.isPending}>
          {action.label}
        </Button>
      )}
    </div>
  )
}

/**
 * Shown when a refresh fails but the previous data is still on screen. React
 * Query keeps the last successful result, so claiming "no data" there would
 * contradict the rows rendered right below it.
 */
export function StaleNotice({ onRetry, className }: { onRetry: () => void; className?: string }) {
  return (
    <div
      role="status"
      className={cn(
        'flex items-center gap-2 rounded-lg border border-warning/30 bg-warning/10 px-3 py-2 text-xs',
        className,
      )}
    >
      <AlertTriangle className="size-3.5 shrink-0 text-warning-fg" />
      <span className="flex-1 text-warning-fg">Falha ao atualizar — exibindo os últimos dados recebidos.</span>
      <Button variant="ghost" size="sm" className="h-6 px-2" onClick={onRetry}>
        Tentar novamente
      </Button>
    </div>
  )
}
