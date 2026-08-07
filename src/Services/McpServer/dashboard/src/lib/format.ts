export function formatDateTime(value?: string | null) {
  if (!value) return '—'
  return new Date(value).toLocaleString('pt-BR', {
    day: '2-digit',
    month: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

export function formatNumber(value: number | null | undefined, unit?: string | null) {
  if (value == null || !Number.isFinite(value)) return '—'
  if (unit === 'ratio') return `${(value * 100).toLocaleString('pt-BR', { maximumFractionDigits: 2 })}%`
  if (unit === 'bytes') {
    const units = ['B', 'KiB', 'MiB', 'GiB']
    let current = value
    let index = 0
    while (Math.abs(current) >= 1024 && index < units.length - 1) {
      current /= 1024
      index++
    }
    return `${current.toLocaleString('pt-BR', { maximumFractionDigits: 1 })} ${units[index]}`
  }
  const formatted = value.toLocaleString('pt-BR', { maximumFractionDigits: 2 })
  return unit ? `${formatted} ${unit}` : formatted
}

export function formatPercent(value: number | null | undefined, digits = 0) {
  if (value == null || !Number.isFinite(value)) return '—'
  return `${(value * 100).toLocaleString('pt-BR', {
    minimumFractionDigits: digits,
    maximumFractionDigits: digits,
  })}%`
}

export function humanize(value: string) {
  return value
    .replace(/_/g, ' ')
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/^./, (character) => character.toUpperCase())
}
