import { describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client'
import { applyServerErrors, passwordSchema } from './forms'

describe('password policy (mirrors the API)', () => {
  it.each([
    ['short1A', false],
    ['alllowercase1', false],
    ['ALLUPPERCASE1', false],
    ['NoDigitsHere', false],
    ['Valid-Pass-123', true],
  ])('%s → %s', (value, ok) => {
    expect(passwordSchema.safeParse(value).success).toBe(ok)
  })
})

describe('applyServerErrors', () => {
  it('maps API field errors onto form fields', () => {
    const setError = vi.fn()
    const error = new ApiError(400, { detail: 'Invalid', errors: { email: ['Email is taken.'] } }, 'x')
    expect(applyServerErrors(error, setError, ['email', 'password'])).toBeNull()
    expect(setError).toHaveBeenCalledWith('email', { type: 'server', message: 'Email is taken.' })
  })

  it('returns the message when there are no field errors', () => {
    const error = new ApiError(409, { detail: 'That car was just booked.' }, 'x')
    expect(applyServerErrors(error, vi.fn(), ['email'])).toBe('That car was just booked.')
  })
})
