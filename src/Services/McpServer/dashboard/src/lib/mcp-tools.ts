import {
  Activity,
  BrainCircuit,
  Clock3,
  FileJson,
  Gauge,
  GitFork,
  HeartPulse,
  ScanSearch,
} from 'lucide-react'
import type { ComponentType } from 'react'

export interface McpTool {
  name: string
  description: string
  sources: string[]
  icon: ComponentType<{ className?: string }>
}

/**
 * Product-facing list of the eight vNext tools. Legacy tools remain registered
 * for compatibility, but are intentionally omitted here so new clients are
 * guided toward versioned envelopes and deterministic engines.
 */
export const MCP_TOOLS: McpTool[] = [
  {
    name: 'service_get_spec',
    description: 'Resume identidade, ownership, imagem, cobertura, contratos e dependências declaradas.',
    sources: ['Catalog', 'Kubernetes', 'OpenAPI'],
    icon: FileJson,
  },
  {
    name: 'service_get_health',
    description: 'Consolida RED, recursos e estabilidade em findings explicáveis por janela.',
    sources: ['Kubernetes', 'Prometheus'],
    icon: HeartPulse,
  },
  {
    name: 'service_get_score',
    description: 'Retorna score, healthStatus, cobertura e dimensões sem recalcular regras no cliente.',
    sources: ['Health Engine'],
    icon: Gauge,
  },
  {
    name: 'service_get_dependencies',
    description: 'Resolve upstream, downstream, causalidade, ciclos, caminho crítico e blast radius.',
    sources: ['Catalog', 'Jaeger'],
    icon: GitFork,
  },
  {
    name: 'service_detect_anomalies',
    description: 'Compara janela atual, anterior, 24h e 7d com baseline robusto e confiança explícita.',
    sources: ['Prometheus'],
    icon: ScanSearch,
  },
  {
    name: 'service_get_incident_timeline',
    description: 'Ordena e deduplica deploys, anomalias, eventos, restarts, traces e logs correlatos.',
    sources: ['Events', 'Prometheus', 'Jaeger', 'Loki'],
    icon: Clock3,
  },
  {
    name: 'service_find_root_cause',
    description: 'Classifica hipóteses com evidências favoráveis e contrárias, limitações e confiança.',
    sources: ['RCA Engine'],
    icon: BrainCircuit,
  },
  {
    name: 'system_get_health_summary',
    description: 'Prioriza a saúde de todos os serviços habilitados usando o mesmo cache do Health Engine.',
    sources: ['Health Engine', 'Catalog'],
    icon: Activity,
  },
]

/** Standard port exposed by the up-k8s port-forward workflow. */
export const MCP_CLIENT_CONFIG = `{
  "servers": {
    "mcp-apis": {
      "url": "http://localhost:4000/",
      "type": "streamable-http"
    }
  }
}`
