import { useEffect, useRef, useState } from 'react'
import { Check, Copy, FileCode2, Plug, Terminal } from 'lucide-react'

import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Reveal } from '@/components/ui/reveal'
import { SectionHeading } from '@/components/landing/SectionHeading'
import { MCP_CLIENT_CONFIG } from '@/lib/mcp-tools'

const STEPS = [
  {
    icon: Terminal,
    title: 'Suba o cluster',
    body: '.\\infra\\scripts\\ps\\up-k8s.ps1 -Build valida o WSL, sobe o k3d, importa as imagens e executa health checks.',
  },
  {
    icon: FileCode2,
    title: 'Aponte seu cliente',
    body: 'Salve a configuração ao lado em .vscode/mcp.json na raiz do projeto.',
  },
  {
    icon: Plug,
    title: 'Pergunte ao agente',
    body: '“Qual a saúde da ProdutoAPI?” ou “Encontre a causa raiz da regressão após o último deploy.”',
  },
]

function CopyButton({ value }: { value: string }) {
  const [copied, setCopied] = useState(false)
  const timer = useRef<ReturnType<typeof setTimeout> | undefined>(undefined)

  // Clear the pending reset if the section unmounts mid-countdown.
  useEffect(() => () => clearTimeout(timer.current), [])

  const copy = async () => {
    try {
      await navigator.clipboard.writeText(value)
      setCopied(true)
      clearTimeout(timer.current)
      timer.current = setTimeout(() => setCopied(false), 2000)
    } catch {
      // Clipboard needs a secure context; leave the block selectable as fallback.
      setCopied(false)
    }
  }

  return (
    <Button variant="outline" size="sm" onClick={copy} aria-label="Copiar configuração do cliente MCP">
      {copied ? <Check className="size-3.5 text-success-fg" /> : <Copy className="size-3.5" />}
      {copied ? 'Copiado' : 'Copiar'}
    </Button>
  )
}

export function ConnectSection() {
  return (
    <section id="conectar" className="scroll-mt-32" aria-labelledby="conectar-heading">
      <SectionHeading
        id="conectar-heading"
        eyebrow="Começar"
        title="Conecte um agente em três passos"
        description="O transporte é Streamable HTTP (protocolo MCP 2025-03-26), então qualquer cliente compatível serve."
      />

      <div className="mt-6 grid grid-cols-1 gap-4 lg:grid-cols-2">
        <Reveal>
          <ol className="flex h-full flex-col gap-3">
            {STEPS.map((step, i) => {
              const Icon = step.icon
              return (
                <li key={step.title}>
                  <Card className="h-full">
                    <CardContent className="flex items-start gap-3 p-4">
                      <div className="flex size-8 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
                        <Icon className="size-4" />
                      </div>
                      <div className="min-w-0">
                        <p className="text-sm font-medium">
                          <span className="mr-1.5 text-muted-foreground tabular">{i + 1}.</span>
                          {step.title}
                        </p>
                        <p className="mt-0.5 text-xs leading-relaxed text-muted-foreground">{step.body}</p>
                      </div>
                    </CardContent>
                  </Card>
                </li>
              )
            })}
          </ol>
        </Reveal>

        <Reveal delay={100}>
          <Card className="h-full overflow-hidden">
            <div className="flex items-center justify-between gap-2 border-b border-border bg-muted/40 px-4 py-2.5">
              <code className="font-mono text-xs text-muted-foreground">.vscode/mcp.json</code>
              <CopyButton value={MCP_CLIENT_CONFIG} />
            </div>
            <CardContent className="p-0">
              <pre className="overflow-x-auto scrollbar-thin p-4 text-xs leading-relaxed">
                <code className="font-mono">{MCP_CLIENT_CONFIG}</code>
              </pre>
            </CardContent>
          </Card>
        </Reveal>
      </div>
    </section>
  )
}
