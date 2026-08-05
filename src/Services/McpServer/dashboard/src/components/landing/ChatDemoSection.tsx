import { useEffect, useState } from 'react'
import type { ReactNode } from 'react'
import { ArrowRight, Bot, User } from 'lucide-react'

import { Badge } from '@/components/ui/badge'
import { Card, CardContent } from '@/components/ui/card'
import { Reveal } from '@/components/ui/reveal'
import { SectionHeading } from '@/components/landing/SectionHeading'
import { MCP_TOOLS } from '@/lib/mcp-tools'
import { cn } from '@/lib/utils'

interface ChatExample {
  tool: string
  question: string
  answer: ReactNode
}

/** Scripted, but grounded in the real tool names/queries — not a live agent call. */
const EXAMPLES: ChatExample[] = [
  {
    tool: 'get_health',
    question: 'A ProdutoAPI está saudável?',
    answer: (
      <div className="space-y-2.5">
        <p className="leading-relaxed">
          <strong className="font-semibold text-success-fg">Saudável.</strong> 2 de 2 pods prontos e
          zero restarts na última hora.
        </p>
        <div className="flex flex-wrap gap-1.5">
          <Badge variant="success">2/2 prontos</Badge>
          <Badge variant="outline">0 restarts</Badge>
          <Badge variant="outline" className="font-mono text-[10px]">
            deployment/produtoapi
          </Badge>
        </div>
      </div>
    ),
  },
  {
    tool: 'find_data_origin',
    question: 'De onde vem o preço da rota /api/products/{id}?',
    answer: (
      <div className="space-y-2.5">
        <p className="leading-relaxed">A origem é uma chamada encadeada até o Postgres:</p>
        <div className="flex flex-wrap items-center gap-1.5">
          <Badge variant="outline" className="font-mono text-[10px]">
            ProdutoAPI
          </Badge>
          <ArrowRight className="size-3 shrink-0 text-muted-foreground" aria-hidden="true" />
          <Badge variant="outline" className="font-mono text-[10px]">
            PrecoAPI
          </Badge>
          <ArrowRight className="size-3 shrink-0 text-muted-foreground" aria-hidden="true" />
          <Badge variant="outline" className="font-mono text-[10px]">
            preco_db
          </Badge>
        </div>
      </div>
    ),
  },
  {
    tool: 'query_metrics',
    question: 'Tem erro 5xx na PrecoAPI nos últimos 5 minutos?',
    answer: (
      <div className="space-y-2.5">
        <p className="leading-relaxed">
          <strong className="font-semibold text-success-fg">Nenhum.</strong> A consulta não retornou
          amostras no intervalo:
        </p>
        <pre className="overflow-x-auto rounded-md bg-muted/60 px-2.5 py-2 font-mono text-[10px] leading-relaxed">
          <code>{'sum(rate(..._count{service="precoapi", ...status_code=~"5.."}[5m]))'}</code>
        </pre>
        <Badge variant="success">0 erros</Badge>
      </div>
    ),
  },
]

const TYPE_MS_PER_CHAR = 26
const THINKING_MS = 850
const HOLD_MS = 3800

type Phase = 'typing' | 'thinking' | 'answered'

/**
 * A scripted chat mockup: the question types itself out, the MCP "thinks" for a
 * beat, then a formatted answer lands — cycling through a few real use cases.
 * Fully static (first example, fully typed) under prefers-reduced-motion.
 */
export function ChatDemoSection() {
  const [reducedMotion] = useState(
    () => typeof window !== 'undefined' && window.matchMedia('(prefers-reduced-motion: reduce)').matches,
  )
  const [index, setIndex] = useState(0)
  const [phase, setPhase] = useState<Phase>(() => (reducedMotion ? 'answered' : 'typing'))
  const [typedLength, setTypedLength] = useState(() => (reducedMotion ? EXAMPLES[0].question.length : 0))

  const example = EXAMPLES[index]
  const tool = MCP_TOOLS.find((t) => t.name === example.tool)
  const ToolIcon = tool?.icon

  // Resets the typewriter whenever the example changes (auto-advance or manual pick).
  useEffect(() => {
    if (reducedMotion) {
      setPhase('answered')
      setTypedLength(EXAMPLES[index].question.length)
      return
    }
    setTypedLength(0)
    setPhase('typing')
  }, [index, reducedMotion])

  // Types the question one character at a time, then hands off to "thinking".
  useEffect(() => {
    if (reducedMotion || phase !== 'typing') return
    if (typedLength >= example.question.length) {
      const t = setTimeout(() => setPhase('thinking'), 250)
      return () => clearTimeout(t)
    }
    const t = setTimeout(() => setTypedLength((n) => n + 1), TYPE_MS_PER_CHAR)
    return () => clearTimeout(t)
  }, [phase, typedLength, reducedMotion, example.question])

  // Brief "typing…" bubble before the formatted answer lands.
  useEffect(() => {
    if (reducedMotion || phase !== 'thinking') return
    const t = setTimeout(() => setPhase('answered'), THINKING_MS)
    return () => clearTimeout(t)
  }, [phase, reducedMotion])

  // Holds the answer on screen, then cycles to the next use case.
  useEffect(() => {
    if (reducedMotion || phase !== 'answered') return
    const t = setTimeout(() => setIndex((i) => (i + 1) % EXAMPLES.length), HOLD_MS)
    return () => clearTimeout(t)
  }, [phase, reducedMotion])

  return (
    <section aria-labelledby="chat-heading">
      <SectionHeading
        id="chat-heading"
        eyebrow="Na prática"
        title="Uma pergunta em português, uma resposta correlacionada"
        description="O agente decide qual ferramenta chamar, o MCP busca nas fontes certas e devolve texto pronto — não um JSON cru para o modelo interpretar sozinho."
      />

      <Reveal delay={80} className="mt-6">
        <Card className="overflow-hidden">
          <div className="flex items-center justify-between gap-2 border-b border-border bg-muted/40 px-4 py-2.5">
            <div className="flex items-center gap-2">
              <span className="flex size-2 shrink-0 rounded-full bg-success" aria-hidden="true" />
              <code className="font-mono text-xs text-muted-foreground">Cliente MCP · streamable-http</code>
            </div>
            {ToolIcon && (
              <Badge variant="outline" className="gap-1 font-mono text-[10px]">
                <ToolIcon className="size-3" />
                {example.tool}
              </Badge>
            )}
          </div>

          <CardContent className="flex min-h-60 flex-col justify-end gap-3 p-4 sm:p-6">
            <div className="flex items-start justify-end gap-2.5">
              <div className="max-w-[85%] rounded-2xl rounded-tr-sm bg-primary px-3.5 py-2.5 text-sm text-primary-foreground">
                {example.question.slice(0, typedLength)}
                {phase === 'typing' && (
                  <span className="typing-caret" aria-hidden="true">
                    ▍
                  </span>
                )}
              </div>
              <div
                className="flex size-7 shrink-0 items-center justify-center rounded-full bg-muted"
                aria-hidden="true"
              >
                <User className="size-3.5" />
              </div>
            </div>

            {phase !== 'typing' && (
              <div className="flex items-start gap-2.5">
                <div
                  className="flex size-7 shrink-0 items-center justify-center rounded-full bg-primary/15 text-primary"
                  aria-hidden="true"
                >
                  <Bot className="size-3.5" />
                </div>
                {phase === 'thinking' ? (
                  <div
                    role="status"
                    aria-label="MCP está respondendo"
                    className="flex items-center gap-1 rounded-2xl rounded-tl-sm border border-border bg-card px-3.5 py-3"
                  >
                    <span className="typing-dot" />
                    <span className="typing-dot" />
                    <span className="typing-dot" />
                  </div>
                ) : (
                  <div className="max-w-[85%] rounded-2xl rounded-tl-sm border border-border bg-card px-3.5 py-3 text-sm">
                    {example.answer}
                  </div>
                )}
              </div>
            )}
          </CardContent>

          <div className="flex items-center justify-center gap-1.5 border-t border-border py-2.5">
            {EXAMPLES.map((ex, i) => (
              <button
                key={ex.tool}
                type="button"
                onClick={() => setIndex(i)}
                aria-label={`Ver exemplo: ${ex.question}`}
                aria-current={i === index}
                className={cn(
                  'h-1.5 cursor-pointer rounded-full transition-all',
                  i === index ? 'w-5 bg-primary' : 'w-1.5 bg-border hover:bg-muted-foreground/40',
                )}
              />
            ))}
          </div>
        </Card>
      </Reveal>
    </section>
  )
}
