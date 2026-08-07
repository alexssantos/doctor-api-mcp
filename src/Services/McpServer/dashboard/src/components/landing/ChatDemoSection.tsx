import { useCallback, useEffect, useState } from 'react'
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
    tool: 'service_get_health',
    question: 'A ProdutoAPI está saudável?',
    answer: (
      <div className="space-y-2.5">
        <p className="leading-relaxed">
          <strong className="font-semibold text-warning-fg">Degradada, score 72.</strong> Os pods estão
          prontos, mas o P95 subiu 38% na janela de 30 minutos.
        </p>
        <div className="flex flex-wrap gap-1.5">
          <Badge variant="success">2/2 prontos</Badge>
          <Badge variant="warning">P95 +38%</Badge>
          <Badge variant="outline">cobertura 92%</Badge>
        </div>
      </div>
    ),
  },
  {
    tool: 'service_get_incident_timeline',
    question: 'O que aconteceu depois do deploy da PrecoAPI?',
    answer: (
      <div className="space-y-2.5">
        <p className="leading-relaxed">A timeline correlacionou três eventos sem duplicatas:</p>
        <div className="flex flex-wrap items-center gap-1.5">
          <Badge variant="outline" className="font-mono text-[10px]">
            14:08 deploy rev.17
          </Badge>
          <ArrowRight className="size-3 shrink-0 text-muted-foreground" aria-hidden="true" />
          <Badge variant="warning" className="font-mono text-[10px]">
            14:12 P95 anômalo
          </Badge>
          <ArrowRight className="size-3 shrink-0 text-muted-foreground" aria-hidden="true" />
          <Badge variant="destructive" className="font-mono text-[10px]">
            14:14 erros 5xx
          </Badge>
        </div>
      </div>
    ),
  },
  {
    tool: 'service_find_root_cause',
    question: 'Qual é a causa raiz mais provável?',
    answer: (
      <div className="space-y-2.5">
        <p className="leading-relaxed">
          <strong className="font-semibold text-warning-fg">Hipótese principal, confiança 86%:</strong>{' '}
          regressão introduzida pela revisão 17.
        </p>
        <div className="flex flex-wrap gap-1.5">
          <Badge variant="outline">3 evidências favoráveis</Badge>
          <Badge variant="outline">0 contrárias</Badge>
          <Badge variant="secondary">recomendação somente leitura</Badge>
        </div>
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

  const selectExample = useCallback((nextIndex: number) => {
    setIndex(nextIndex)
    if (reducedMotion) {
      setPhase('answered')
      setTypedLength(EXAMPLES[nextIndex].question.length)
      return
    }
    setTypedLength(0)
    setPhase('typing')
  }, [reducedMotion])

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
    const t = setTimeout(() => selectExample((index + 1) % EXAMPLES.length), HOLD_MS)
    return () => clearTimeout(t)
  }, [index, phase, reducedMotion, selectExample])

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

          <div className="flex items-center justify-center border-t border-border py-1">
            {EXAMPLES.map((ex, i) => (
              <button
                key={ex.tool}
                type="button"
                onClick={() => selectExample(i)}
                aria-label={`Ver exemplo: ${ex.question}`}
                aria-current={i === index}
                className={cn(
                  'group inline-flex size-11 cursor-pointer items-center justify-center rounded-md focus-visible:ring-2 focus-visible:ring-ring focus-visible:outline-none',
                )}
              >
                <span
                  aria-hidden="true"
                  className={cn(
                    'h-1.5 rounded-full transition-all',
                    i === index ? 'w-5 bg-primary' : 'w-1.5 bg-border group-hover:bg-muted-foreground/40',
                  )}
                />
              </button>
            ))}
          </div>
        </Card>
      </Reveal>
    </section>
  )
}
