import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Reveal } from '@/components/ui/reveal'
import { MCP_TOOLS } from '@/lib/mcp-tools'
import { SectionHeading } from '@/components/landing/SectionHeading'

const SOURCE_TONE: Record<string, string> = {
  Kubernetes: 'text-chart-1',
  Jaeger: 'text-chart-2',
  Prometheus: 'text-chart-3',
  OpenAPI: 'text-chart-4',
  ApplicationCatalog: 'text-chart-5',
}

export function ToolsGrid() {
  return (
    <section aria-labelledby="ferramentas-heading">
      <SectionHeading
        id="ferramentas-heading"
        eyebrow="Ferramentas"
        title="9 perguntas que o agente já sabe responder"
        description="Cada ferramenta é uma consulta pronta sobre o cluster: o MCP busca nas fontes, correlaciona e devolve a análise — o modelo não precisa saber que Jaeger ou Prometheus existem."
      />

      <div className="mt-6 grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-3">
        {MCP_TOOLS.map((tool, i) => {
          const Icon = tool.icon
          return (
            <Reveal key={tool.name} delay={i * 60}>
              <Card className="h-full transition-colors hover:border-primary/40">
                <CardContent className="flex h-full flex-col gap-2 p-4">
                  <div className="flex items-center gap-2">
                    <div className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-muted">
                      <Icon className="size-4 text-foreground" />
                    </div>
                    <code className="min-w-0 truncate font-mono text-xs font-medium">{tool.name}</code>
                  </div>

                  <p className="flex-1 text-xs leading-relaxed text-muted-foreground">{tool.description}</p>

                  <div className="flex flex-wrap gap-1.5">
                    {tool.sources.map((source) => (
                      <Badge key={source} variant="outline" className={SOURCE_TONE[source] ?? ''}>
                        {source}
                      </Badge>
                    ))}
                  </div>
                </CardContent>
              </Card>
            </Reveal>
          )
        })}
      </div>
    </section>
  )
}
