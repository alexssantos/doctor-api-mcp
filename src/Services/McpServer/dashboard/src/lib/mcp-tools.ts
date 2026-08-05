import {
  Activity,
  FileJson,
  GitFork,
  HeartPulse,
  Layers,
  ListTree,
  Radar,
  Route,
  Search,
} from 'lucide-react'
import type { ComponentType } from 'react'

export interface McpTool {
  name: string
  description: string
  sources: string[]
  icon: ComponentType<{ className?: string }>
}

/**
 * Mirrors the `WithTools<>` registrations in McpServer/Program.cs. There is no
 * `/api/dashboard` endpoint that lists tools, so this stays a hand-kept copy —
 * update it whenever a tool is added or removed on the server.
 */
export const MCP_TOOLS: McpTool[] = [
  {
    name: 'list_services',
    description: 'Lista services, pods e deployments do namespace com o status de cada um.',
    sources: ['Kubernetes', 'OpenAPI'],
    icon: Layers,
  },
  {
    name: 'list_discovered_applications',
    description:
      'Inventário das aplicações auto-descobertas: fontes de detecção, estado do toggle e por que algo não é indexável.',
    sources: ['ApplicationCatalog'],
    icon: Radar,
  },
  {
    name: 'get_openapi',
    description: 'Devolve a spec OpenAPI completa de um serviço indexado.',
    sources: ['OpenAPI'],
    icon: FileJson,
  },
  {
    name: 'trace_route',
    description: 'Busca traces recentes de um serviço ou rota, com call chain e timings.',
    sources: ['Jaeger'],
    icon: Route,
  },
  {
    name: 'explain_api',
    description: 'Explica o que uma API faz cruzando a spec OpenAPI com traces reais de produção.',
    sources: ['OpenAPI', 'Jaeger'],
    icon: ListTree,
  },
  {
    name: 'get_health',
    description: 'Verifica a saúde de um serviço pelo estado dos pods (ready, restarts).',
    sources: ['Kubernetes'],
    icon: HeartPulse,
  },
  {
    name: 'find_dependencies',
    description: 'Monta o mapa de dependências entre serviços a partir do grafo do Jaeger.',
    sources: ['Jaeger'],
    icon: GitFork,
  },
  {
    name: 'find_data_origin',
    description: 'Rastreia a origem dos dados de uma rota: API → chamadas HTTP → queries SQL.',
    sources: ['OpenAPI', 'Jaeger', 'Kubernetes'],
    icon: Search,
  },
  {
    name: 'query_metrics',
    description: 'Executa PromQL arbitrário contra o Prometheus, instantâneo ou em intervalo.',
    sources: ['Prometheus'],
    icon: Activity,
  },
]

/** Matches the checked-in .vscode/mcp.json at the repo root. */
export const MCP_CLIENT_CONFIG = `{
  "servers": {
    "mcp-apis": {
      "url": "http://localhost:4000/",
      "type": "streamable-http"
    }
  }
}`
