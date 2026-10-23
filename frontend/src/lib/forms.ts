import type { FieldValues, Path, UseFormSetError } from 'react-hook-form'
import { z } from 'zod'
import { ApiError } from '../api/client'

/** Mirrors the server's password policy so users see problems before submitting. */
export const passwordSchema = z.string()
  .min(10, 'Use at least 10 characters.')
  .max(128, 'Use at most 128 characters.')
  .regex(/[A-Z]/, 'Include an uppercase letter.')
  .regex(/[a-z]/, 'Include a lowercase letter.')
  .regex(/[0-9]/, 'Include a number.')

export const phoneSchema = z.string().max(30).regex(/^[+0-9 ()-]*$/, 'Enter a valid phone number.')

/**
 * Copies server-side validation errors (camelCase field keys) onto the form fields.
 * Returns the message to show when an error is not tied to a field.
 */
export function applyServerErrors<T extends FieldValues>(error: unknown, setError: UseFormSetError<T>, fields: readonly string[]): string | null {
  if (error instanceof ApiError) {
    let unmatched = false
    for (const [key, messages] of Object.entries(error.fieldErrors)) {
      const name = key.charAt(0).toLowerCase() + key.slice(1)
      if (fields.includes(name)) setError(name as Path<T>, { type: 'server', message: messages[0] })
      else unmatched = true
    }
    if (Object.keys(error.fieldErrors).length === 0 || unmatched) return error.message
    return null
  }
  return error instanceof Error ? error.message : 'Something went wrong.'
}
