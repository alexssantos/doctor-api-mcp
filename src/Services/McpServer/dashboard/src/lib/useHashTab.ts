import { useCallback, useEffect, useState } from 'react'

/**
 * Keeps the active tab in `location.hash` so a view is shareable and survives a
 * reload. Falls back to the first valid tab when the hash is absent or unknown.
 */
export function useHashTab<T extends string>(tabs: readonly T[], fallback: T) {
  const readHash = useCallback(() => window.location.hash.replace(/^#\/?/, ''), [])

  const [tab, setTab] = useState<T>(() => {
    const hash = readHash()
    return tabs.includes(hash as T) ? (hash as T) : fallback
  })

  // Back/forward and hand-edited URLs both surface as hashchange. In-page
  // anchors (e.g. #conectar) also land here — those are not tabs, so ignore
  // them rather than bouncing the user back to the fallback tab.
  useEffect(() => {
    const onHashChange = () => {
      const hash = readHash()
      if (tabs.includes(hash as T)) setTab(hash as T)
    }
    window.addEventListener('hashchange', onHashChange)
    return () => window.removeEventListener('hashchange', onHashChange)
  }, [readHash, tabs])

  const select = useCallback(
    (next: string) => {
      setTab(next as T)
      if (readHash() !== next) {
        window.location.hash = next
      }
    },
    [readHash],
  )

  return [tab, select] as const
}
