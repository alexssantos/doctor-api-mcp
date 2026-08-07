import { useEffect, useState } from 'react'
import { Box, Check, Copy, ExternalLink, PackageOpen, Terminal, Waypoints } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { Reveal } from '@/components/ui/reveal'
import { SectionHeading } from '@/components/landing/SectionHeading'
import { cn } from '@/lib/utils'

const INSTALL_OPTIONS = [
  {
    id: 'shell',
    label: 'Linux / WSL',
    icon: Terminal,
    command: 'curl -fsSL https://raw.githubusercontent.com/alexssantos/doctor-api-mcp/master/install.sh | bash',
    detail: 'Usa o contexto Kubernetes atual e instala o chart com Helm.',
  },
  {
    id: 'powershell',
    label: 'PowerShell',
    icon: Waypoints,
    command: 'irm https://raw.githubusercontent.com/alexssantos/doctor-api-mcp/master/install.ps1 | iex',
    detail: 'Detecta helm nativo ou delega automaticamente para o WSL.',
  },
  {
    id: 'helm',
    label: 'Helm',
    icon: PackageOpen,
    command: 'helm upgrade --install doctor-api-mcp oci://registry-1.docker.io/alexssantos/doctor-api-mcp-chart --version 0.1.1 -n mcp-apis --create-namespace',
    detail: 'Ideal para GitOps e clusters com valores próprios de observabilidade.',
  },
] as const

type InstallOptionId = (typeof INSTALL_OPTIONS)[number]['id']

async function writeClipboard(value: string) {
  if (navigator.clipboard?.writeText) {
    await navigator.clipboard.writeText(value)
    return
  }

  const input = document.createElement('textarea')
  input.value = value
  input.setAttribute('readonly', '')
  input.style.position = 'fixed'
  input.style.opacity = '0'
  document.body.appendChild(input)
  input.select()
  document.execCommand('copy')
  input.remove()
}
export function InstallSection() {
  const [selectedId, setSelectedId] = useState<InstallOptionId>('shell')
  const [copied, setCopied] = useState(false)
  const selected = INSTALL_OPTIONS.find((option) => option.id === selectedId) ?? INSTALL_OPTIONS[0]

  useEffect(() => {
    if (!copied) return
    const timeout = window.setTimeout(() => setCopied(false), 2200)
    return () => window.clearTimeout(timeout)
  }, [copied])

  async function copyCommand() {
    await writeClipboard(selected.command)
    setCopied(true)
  }

  return (
    <section id="instalar" aria-labelledby="instalar-heading" className="scroll-mt-24">
      <SectionHeading
        id="instalar-heading"
        eyebrow="Comece agora"
        title="Do zero ao radar no cluster em uma linha"
        description="Escolha seu terminal. O instalador usa o contexto Kubernetes atual, cria o namespace e aguarda o MCP Server ficar pronto."
      />

      <Reveal delay={60} className="mt-6">
        <Card className="overflow-hidden border-primary/30 bg-gradient-to-br from-primary/5 via-card to-chart-2/5">
          <CardContent className="p-0">
            <div className="grid gap-0 lg:grid-cols-[minmax(0,1.55fr)_minmax(17rem,0.75fr)]">
              <div className="min-w-0 p-4 sm:p-6">
                <div className="mb-4 flex flex-wrap items-center gap-2">
                  <Badge variant="secondary">Quick start</Badge>
                  <span className="text-xs text-muted-foreground">Pré-requisitos: Kubernetes + Helm 3</span>
                </div>

                <div className="grid gap-2 sm:grid-cols-3" role="group" aria-label="Escolha o método de instalação">
                  {INSTALL_OPTIONS.map((option) => {
                    const Icon = option.icon
                    const active = option.id === selected.id
                    return (
                      <button
                        key={option.id}
                        type="button"
                        aria-pressed={active}
                        onClick={() => {
                          setSelectedId(option.id)
                          setCopied(false)
                        }}
                        className={cn(
                          'flex min-h-11 cursor-pointer items-center justify-center gap-2 rounded-lg border px-3 py-2 text-sm font-medium transition-colors focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none',
                          active
                            ? 'border-primary bg-primary text-primary-foreground'
                            : 'border-border bg-card hover:bg-accent hover:text-accent-foreground',
                        )}
                      >
                        <Icon className="size-4" aria-hidden="true" />
                        {option.label}
                      </button>
                    )
                  })}
                </div>

                <div className="mt-3 rounded-xl border border-border bg-background/80 p-3 sm:p-4">
                  <div className="flex items-start gap-3">
                    <code className="min-w-0 flex-1 break-all font-mono text-xs leading-relaxed text-foreground sm:text-sm">
                      {selected.command}
                    </code>
                    <Button
                      type="button"
                      variant="outline"
                      size="icon"
                      onClick={copyCommand}
                      aria-label={copied ? 'Comando copiado' : `Copiar comando de ${selected.label}`}
                      className="shrink-0"
                    >
                      {copied ? <Check className="size-4 text-success" /> : <Copy className="size-4" />}
                    </Button>
                  </div>
                  <p className="mt-3 text-xs leading-relaxed text-muted-foreground">{selected.detail}</p>
                  <p className="sr-only" aria-live="polite">{copied ? 'Comando copiado para a área de transferência.' : ''}</p>
                </div>
              </div>

              <aside className="border-t border-border bg-muted/35 p-4 sm:p-6 lg:border-t-0 lg:border-l">
                <div className="flex size-10 items-center justify-center rounded-xl bg-primary/15 text-primary">
                  <Box className="size-5" aria-hidden="true" />
                </div>
                <h4 className="mt-4 text-base font-semibold">Imagem pronta para Kubernetes</h4>
                <p className="mt-2 text-sm leading-relaxed text-muted-foreground">
                  O chart usa a imagem multi-arquitetura do MCP Server e mantém RBAC, probes e estado de indexação sob controle do Helm.
                </p>
                <a
                  href="https://hub.docker.com/r/alexssantos/doctor-api-mcp"
                  target="_blank"
                  rel="noreferrer"
                  className="mt-4 inline-flex min-h-11 items-center gap-2 rounded-md text-sm font-medium text-primary underline-offset-4 hover:underline focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none"
                >
                  alexssantos/doctor-api-mcp
                  <ExternalLink className="size-4" aria-hidden="true" />
                </a>
              </aside>
            </div>
          </CardContent>
        </Card>
      </Reveal>
    </section>
  )
}
