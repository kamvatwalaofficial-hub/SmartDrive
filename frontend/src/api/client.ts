import axios, { AxiosError, type AxiosInstance } from 'axios'
import type { Problem } from './types'

const TOKEN_KEY = 'smartdrive.token'

/** Reads the stored token. Storage can be unavailable (private mode), so every access is guarded. */
export function readToken(): string | null {
  try {
    return window.localStorage.getItem(TOKEN_KEY)
  } catch {
    return null
  }
}

export function writeToken(token: string | null) {
  try {
    if (token) window.localStorage.setItem(TOKEN_KEY, token)
    else window.localStorage.removeItem(TOKEN_KEY)
  } catch {
    /* storage unavailable: the session lasts for this tab only */
  }
}

/** An API failure with the server's RFC 7807 problem details, normalised for display. */
export class ApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly fieldErrors: Record<string, string[]>

  constructor(status: number, problem: Problem | undefined, fallback: string) {
    super(problem?.detail || problem?.title || fallback)
    this.name = 'ApiError'
    this.status = status
    this.code = problem?.code
    this.fieldErrors = problem?.errors ?? {}
  }
}

let onUnauthorised: (() => void) | null = null
export function setUnauthorisedHandler(handler: (() => void) | null) {
  onUnauthorised = handler
}

export const http: AxiosInstance = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL ?? '',
  timeout: 20_000,
  headers: { Accept: 'application/json' },
})

http.interceptors.request.use((config) => {
  const token = readToken()
  if (token) config.headers.Authorization = `Bearer ${token}`
  return config
})

http.interceptors.response.use(
  (response) => response,
  (error: AxiosError<Problem>) => {
    if (!error.response) {
      return Promise.reject(new ApiError(0, undefined, 'Could not reach SmartDrive. Check your connection and try again.'))
    }

    const { status, data } = error.response
    if (status === 401 && readToken()) onUnauthorised?.()
    const fallback =
      status === 429 ? 'Too many requests. Please wait a moment and try again.'
      : status >= 500 ? 'Something went wrong on our side. Please try again.'
      : 'The request could not be completed.'
    return Promise.reject(new ApiError(status, typeof data === 'object' ? data : undefined, fallback))
  },
)

export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) return error.message
  if (error instanceof Error) return error.message
  return 'Something went wrong.'
}
