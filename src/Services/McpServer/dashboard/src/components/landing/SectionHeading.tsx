import { Reveal } from '@/components/ui/reveal'

/** Shared section header so every landing block shares one rhythm. */
export function SectionHeading({
  id,
  eyebrow,
  title,
  description,
}: {
  id?: string
  eyebrow: string
  title: string
  description?: string
}) {
  return (
    <Reveal>
      <p className="text-xs font-semibold uppercase tracking-wider text-primary">{eyebrow}</p>
      <h3 id={id} className="mt-1.5 text-balance text-xl font-semibold tracking-tight sm:text-2xl">
        {title}
      </h3>
      {description && (
        <p className="mt-2 max-w-2xl text-pretty text-sm leading-relaxed text-muted-foreground">{description}</p>
      )}
    </Reveal>
  )
}
