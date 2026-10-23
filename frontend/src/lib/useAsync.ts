import { useEffect, useRef, useState, type DependencyList } from 'react'
import { errorMessage } from '../api/client'

export interface AsyncState<T> {
  data: T | undefined
  error: string | null
  loading: boolean
  reload: () => void
  setData: (value: T) => void
}

/**
 * Runs an async loader whenever its dependencies change (compared by value) and ignores
 * responses that arrive after a newer request was started.
 */
export function useAsync<T>(loader: () => Promise<T>, deps: DependencyList): AsyncState<T> {
  const [data, setData] = useState<T>()
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(true)
  const [tick, setTick] = useState(0)
  const latest = useRef(0)
  const loaderRef = useRef(loader)
  const key = JSON.stringify(deps)

  useEffect(() => { loaderRef.current = loader })

  useEffect(() => {
    const id = ++latest.current
    setLoading(true)
    setError(null)
    loaderRef.current()
      .then((value) => { if (id === latest.current) setData(value) })
      .catch((e: unknown) => { if (id === latest.current) setError(errorMessage(e)) })
      .finally(() => { if (id === latest.current) setLoading(false) })
  }, [key, tick])

  return { data, error, loading, reload: () => setTick((t) => t + 1), setData }
}
