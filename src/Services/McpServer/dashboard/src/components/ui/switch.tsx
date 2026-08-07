import * as React from 'react'
import * as SwitchPrimitive from '@radix-ui/react-switch'

import { cn } from '@/lib/utils'

function Switch({ className, ...props }: React.ComponentProps<typeof SwitchPrimitive.Root>) {
  return (
    <SwitchPrimitive.Root
      data-slot="switch"
      className={cn(
        'peer relative inline-flex size-11 shrink-0 cursor-pointer items-center justify-center rounded-md border border-transparent bg-transparent outline-none transition-colors',
        'before:absolute before:h-5 before:w-9 before:rounded-full before:bg-input before:shadow-xs before:transition-colors',
        'focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2 focus-visible:ring-offset-background',
        'disabled:cursor-not-allowed disabled:opacity-50',
        'data-[state=checked]:before:bg-primary',
        className,
      )}
      {...props}
    >
      <SwitchPrimitive.Thumb
        data-slot="switch-thumb"
        className={cn(
          'pointer-events-none relative block size-4 rounded-full bg-background shadow-sm ring-0 transition-transform',
          'data-[state=checked]:translate-x-2 data-[state=unchecked]:-translate-x-2',
        )}
      />
    </SwitchPrimitive.Root>
  )
}

export { Switch }
