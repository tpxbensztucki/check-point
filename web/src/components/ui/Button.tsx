import type { ButtonHTMLAttributes } from 'react'

export type ButtonVariant = 'primary' | 'secondary' | 'destructive'

const VARIANT_CLASSES: Record<ButtonVariant, string> = {
  primary: 'bg-primary text-white hover:bg-primary-dark disabled:opacity-50',
  secondary: 'border border-gray-300 text-gray-700 hover:bg-gray-50 disabled:opacity-50',
  destructive: 'border border-danger text-danger hover:bg-danger-bg disabled:opacity-50',
}

export interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
}

// Shared button primitive (CBLT-319) — replaces the inconsistent hand-rolled
// bg-gray-900/bg-blue-600/border-red-300 combinations previously scattered
// across every page. `type="button"` is the default since most call sites use
// this outside a form; pass `type="submit"` explicitly where needed.
function Button({ variant = 'secondary', type = 'button', className = '', ...rest }: ButtonProps) {
  return (
    <button
      type={type}
      className={`rounded-md px-3 py-1.5 text-sm font-medium whitespace-nowrap ${VARIANT_CLASSES[variant]} ${className}`}
      {...rest}
    />
  )
}

export default Button
