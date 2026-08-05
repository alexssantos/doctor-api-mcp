import { useEffect, useRef, type ReactNode } from 'react'
import { animate, onScroll, utils } from 'animejs'

import { cn } from '@/lib/utils'

/**
 * Fades + lifts its children into view on scroll, driven by anime.js.
 * `delay` staggers siblings (60ms steps read as a wave without dragging).
 */
export function Reveal({
  children,
  delay = 0,
  className,
}: {
  children: ReactNode
  delay?: number
  className?: string
}) {
  const ref = useRef<HTMLDivElement | null>(null)

  useEffect(() => {
    const el = ref.current
    if (!el) return

    // Honour the OS setting: land on the final state without animating.
    if (window.matchMedia('(prefers-reduced-motion: reduce)').matches) {
      utils.set(el, { opacity: 1, y: 0 })
      return
    }

    const animation = animate(el, {
      opacity: [0, 1],
      y: [18, 0],
      duration: 620,
      delay,
      ease: 'out(3)',
      autoplay: onScroll({ enter: 'bottom-=40 top', repeat: false }),
    })

    return () => {
      animation.revert()
    }
  }, [delay])

  return (
    // Starts hidden so nothing flashes before anime.js takes over; the
    // reduced-motion branch and the animation both resolve it to opacity 1.
    <div ref={ref} className={cn('opacity-0', className)}>
      {children}
    </div>
  )
}
