import { AlertTriangle, CheckCircle2, CircleHelp, Info, ShieldAlert, XCircle } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import type {
  AnalysisConclusion,
  ExecutionStatus,
  FindingSeverity,
  HealthState,
  SourceAvailability,
} from '@/lib/api'

export function HealthStatusBadge({ status }: { status: HealthState }) {
  const meta = {
    healthy: { label: 'Saudável', variant: 'success' as const, icon: CheckCircle2 },
    degraded: { label: 'Degradado', variant: 'warning' as const, icon: AlertTriangle },
    critical: { label: 'Crítico', variant: 'destructive' as const, icon: XCircle },
    unknown: { label: 'Desconhecido', variant: 'outline' as const, icon: CircleHelp },
  }[status]
  const Icon = meta.icon
  return (
    <Badge variant={meta.variant}>
      <Icon aria-hidden="true" />
      {meta.label}
    </Badge>
  )
}

export function ExecutionStatusBadge({ status }: { status: ExecutionStatus }) {
  const meta = {
    complete: { label: 'Completo', variant: 'success' as const, icon: CheckCircle2 },
    partial: { label: 'Parcial', variant: 'warning' as const, icon: AlertTriangle },
    unavailable: { label: 'Indisponível', variant: 'destructive' as const, icon: XCircle },
  }[status]
  const Icon = meta.icon
  return (
    <Badge variant={meta.variant}>
      <Icon aria-hidden="true" />
      {meta.label}
    </Badge>
  )
}

export function SeverityBadge({ severity }: { severity: FindingSeverity }) {
  const meta = {
    info: { label: 'Info', variant: 'outline' as const, icon: Info },
    warning: { label: 'Atenção', variant: 'warning' as const, icon: AlertTriangle },
    critical: { label: 'Crítico', variant: 'destructive' as const, icon: ShieldAlert },
  }[severity]
  const Icon = meta.icon
  return (
    <Badge variant={meta.variant}>
      <Icon aria-hidden="true" />
      {meta.label}
    </Badge>
  )
}

export function AvailabilityBadge({ availability }: { availability: SourceAvailability }) {
  const meta = {
    available: { label: 'Disponível', variant: 'success' as const, icon: CheckCircle2 },
    stale: { label: 'Desatualizada', variant: 'warning' as const, icon: AlertTriangle },
    unavailable: { label: 'Indisponível', variant: 'outline' as const, icon: XCircle },
  }[availability]
  const Icon = meta.icon
  return (
    <Badge variant={meta.variant}>
      <Icon aria-hidden="true" />
      {meta.label}
    </Badge>
  )
}

export function ConclusionBadge({ conclusion }: { conclusion: AnalysisConclusion }) {
  const meta = {
    detected: { label: 'Detectado', variant: 'warning' as const, icon: AlertTriangle },
    notDetected: { label: 'Não detectado', variant: 'success' as const, icon: CheckCircle2 },
    inconclusive: { label: 'Inconclusivo', variant: 'outline' as const, icon: CircleHelp },
  }[conclusion]
  const Icon = meta.icon
  return (
    <Badge variant={meta.variant}>
      <Icon aria-hidden="true" />
      {meta.label}
    </Badge>
  )
}
