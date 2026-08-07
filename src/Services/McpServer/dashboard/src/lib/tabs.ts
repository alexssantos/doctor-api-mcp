/**
 * Tab identity lives outside the Header component so the constant can be shared
 * with App.tsx without breaking React Fast Refresh.
 */
export const TABS = ['visao', 'servico', 'incidente', 'catalogo', 'projeto'] as const

export type TabId = (typeof TABS)[number]
