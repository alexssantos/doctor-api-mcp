import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'

import '@/index.css'
import { LandingDemo } from '@/components/landing/LandingDemo'

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <LandingDemo />
  </StrictMode>,
)
