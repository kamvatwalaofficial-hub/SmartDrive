import { render } from '@testing-library/react'
import type { ReactElement } from 'react'
import { MemoryRouter, Route, Routes } from 'react-router'
import { AuthProvider } from '../auth/AuthContext'

export function renderAt(ui: ReactElement, { path = '/', route = '/' }: { path?: string; route?: string } = {}) {
  return render(
    <MemoryRouter initialEntries={[route]}>
      <AuthProvider>
        <Routes>
          <Route path={path} element={ui} />
          <Route path="*" element={<p>Navigated away</p>} />
        </Routes>
      </AuthProvider>
    </MemoryRouter>,
  )
}
