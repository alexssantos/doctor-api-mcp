import { ArrowDown, Bot, Boxes, Database, Flame, Server, Waypoints } from 'lucide-react'
import type { ComponentType } from 'react'

import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Reveal } from '@/components/ui/reveal'
import { SectionHeading } from '@/components/landing/SectionHeading'

function FlowNode({
  icon: Icon,
  title,
  subtitle,
  tone,
}: {
  icon: ComponentType<{ className?: string }>
  title: string
  subtitle: string
  tone: string
}) {
  return (
    <div className="flex min-w-0 flex-1 items-center gap-2.5 rounded-lg border border-border bg-background/60 px-3 py-2.5">
      <div className={`flex size-8 shrink-0 items-center justify-center rounded-md ${tone}`}>
        <Icon className="size-4" />
      </div>
      <div className="min-w-0">
        <p className="truncate text-xs font-medium">{title}</p>
        <p className="truncate text-[11px] text-muted-foreground">{subtitle}</p>
      </div>
    </div>
  )
}

function FlowArrow({ label }: { label: string }) {
  return (
    <div className="flex items-center justify-center gap-2 py-1.5" aria-hidden="true">
      <span className="h-px w-8 bg-border" />
      <span className="text-[11px] font-medium text-muted-foreground">{label}</span>
      <ArrowDown className="size-3.5 text-muted-foreground" />
      <span className="h-px w-8 bg-border" />
    </div>
  )
}

export function ArchitectureFlow() {
  return (
    <section aria-labelledby="arquitetura-heading">
      <SectionHeading
        id="arquitetura-heading"
        eyebrow="Arquitetura"
        title="Como o dado chega até o agente"
        description="O MCP Server é um orquestrador sem banco próprio: ele agrega Kubernetes, Jaeger, Prometheus e as specs OpenAPI de cada serviço em respostas únicas."
      />

      <Reveal delay={80} className="mt-6">
        <Card>
          <CardContent className="p-4 sm:p-6">
            <FlowNode
              icon={Bot}
              title="Cliente MCP"
              subtitle="VS Code · Claude · Cursor"
              tone="bg-chart-5/15 text-chart-5"
            />

            <FlowArrow label="POST / · streamable-http" />

            <div className="rounded-lg border border-primary/40 bg-primary/5 p-3">
              <div className="mb-2.5 flex flex-wrap items-center gap-2">
                <Server className="size-4 text-primary" />
                <p className="text-sm font-semibold">McpServer</p>
                <Badge variant="outline">porta 4000</Badge>
                <Badge variant="secondary">9 ferramentas</Badge>
              </div>

              <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 lg:grid-cols-4">
                <FlowNode
                  icon={Boxes}
                  title="Kubernetes API"
                  subtitle="in-cluster, read-only"
                  tone="bg-chart-1/15 text-chart-1"
                />
                <FlowNode
                  icon={Waypoints}
                  title="Jaeger"
                  subtitle="traces e dependências"
                  tone="bg-chart-2/15 text-chart-2"
                />
                <FlowNode
                  icon={Flame}
                  title="Prometheus"
                  subtitle="métricas e PromQL"
                  tone="bg-chart-3/15 text-chart-3"
                />
                <FlowNode
                  icon={Database}
                  title="OpenAPI"
                  subtitle="rotas e schemas"
                  tone="bg-chart-4/15 text-chart-4"
                />
              </div>
            </div>

            <FlowArrow label="descoberta + coleta" />

            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
              <FlowNode
                icon={Server}
                title="ProdutoAPI"
                subtitle="produtos · PostgreSQL produto_db"
                tone="bg-muted text-foreground"
              />
              <FlowNode
                icon={Server}
                title="PrecoAPI"
                subtitle="preços · PostgreSQL preco_db"
                tone="bg-muted text-foreground"
              />
            </div>

            <p className="mt-3 text-center text-[11px] text-muted-foreground">
              ProdutoAPI chama PrecoAPI via HttpClient tipado — a aresta que aparece no grafo de dependências.
            </p>
          </CardContent>
        </Card>
      </Reveal>
    </section>
  )
}
