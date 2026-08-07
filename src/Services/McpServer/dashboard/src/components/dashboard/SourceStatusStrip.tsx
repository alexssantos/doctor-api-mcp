import { Clock3, Database } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import type { SourceStatus } from '@/lib/api'
import { formatDateTime, humanize } from '@/lib/format'

export function SourceStatusStrip({ sources }: { sources: SourceStatus[] }) {
  if (sources.length === 0) return null
  const byName = sources.reduce((groups, source) => {
    const key = source.name.toLowerCase()
    const entries = groups.get(key) ?? []
    entries.push(source)
    groups.set(key, entries)
    return groups
  }, new Map<string, SourceStatus[]>())
  const grouped = Array.from(byName.values(), (entries) => {
      const availability = entries.some((entry) => entry.availability === 'available')
        ? 'available'
        : entries.some((entry) => entry.availability === 'stale')
          ? 'stale'
          : 'unavailable'
      const latest = entries
        .map((entry) => entry.observedAt)
        .filter((value): value is string => Boolean(value))
        .sort()
        .at(-1) ?? null
      return {
        name: entries[0].name,
        availability,
        observedAt: latest,
        freshnessSeconds: Math.max(...entries.map((entry) => entry.freshnessSeconds ?? 0)),
        elapsedMilliseconds: entries.reduce((sum, entry) => sum + entry.elapsedMilliseconds, 0),
        warnings: Array.from(new Set(entries.flatMap((entry) => entry.warnings))),
      } satisfies SourceStatus
    })

  return (
    <div className="flex flex-wrap gap-1.5" aria-label="Estado das fontes de telemetria">
      {grouped.map((source) => (
        <Tooltip key={source.name}>
          <TooltipTrigger asChild>
            <span className="inline-flex">
              <Badge
                variant={
                  source.availability === 'available'
                    ? 'success'
                    : source.availability === 'stale'
                      ? 'warning'
                      : 'outline'
                }
              >
                <Database aria-hidden="true" />
                {humanize(source.name)} · {source.availability === 'available' ? 'ok' : source.availability}
              </Badge>
            </span>
          </TooltipTrigger>
          <TooltipContent className="max-w-xs space-y-1">
            <p className="font-medium">{humanize(source.name)}</p>
            <p className="flex items-center gap-1 text-xs">
              <Clock3 className="size-3" /> observado em {formatDateTime(source.observedAt)}
            </p>
            {source.freshnessSeconds != null && (
              <p className="text-xs">Freshness: {source.freshnessSeconds}s</p>
            )}
            {source.warnings.map((warning) => (
              <p key={warning} className="text-xs text-warning-fg">
                {warning}
              </p>
            ))}
          </TooltipContent>
        </Tooltip>
      ))}
    </div>
  )
}
