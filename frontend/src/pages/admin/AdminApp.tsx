import { BarChart3, CalendarRange, Car, ClipboardList, FolderTree, Home, Inbox, LogOut, Menu, MessageSquareWarning, Users, Wrench, X } from 'lucide-react'
import { useEffect, useState } from 'react'
import { Link, NavLink, Route, Routes, useLocation, useNavigate } from 'react-router'
import { useAuth } from '../../auth/AuthContext'
import { Logo } from '../../components/Layout'
import NotFoundPage from '../NotFoundPage'
import AuditPage from './AuditPage'
import BookingsAdminPage from './BookingsAdminPage'
import CatalogPage from './CatalogPage'
import CustomersPage from './CustomersPage'
import DashboardPage from './DashboardPage'
import { FleetListPage, VehicleEditPage } from './FleetPages'
import InsightsPage from './InsightsPage'
import MaintenancePage from './MaintenancePage'
import MessagesPage from './MessagesPage'
import ReviewsAdminPage from './ReviewsAdminPage'

const nav = [
  { to: '/admin', label: 'Dashboard', icon: Home, end: true },
  { to: '/admin/insights', label: 'Smart Insights', icon: BarChart3 },
  { to: '/admin/bookings', label: 'Bookings', icon: CalendarRange },
  { to: '/admin/fleet', label: 'Fleet', icon: Car },
  { to: '/admin/maintenance', label: 'Maintenance', icon: Wrench },
  { to: '/admin/customers', label: 'Customers', icon: Users },
  { to: '/admin/reviews', label: 'Reviews', icon: MessageSquareWarning },
  { to: '/admin/messages', label: 'Messages', icon: Inbox },
  { to: '/admin/catalog', label: 'Categories & branches', icon: FolderTree },
  { to: '/admin/audit', label: 'Audit log', icon: ClipboardList },
]

export default function AdminApp() {
  const { user, signOut } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()
  const [open, setOpen] = useState(false)
  useEffect(() => setOpen(false), [location.pathname])

  const sidebar = (
    <nav aria-label="Admin" className="flex flex-col gap-0.5">
      {nav.map(({ to, label, icon: Icon, end }) => (
        <NavLink key={to} to={to} end={end} className={({ isActive }) =>
          `flex items-center gap-3 rounded-xl px-3 py-2 text-sm font-semibold transition ${isActive ? 'bg-amber-500 text-ink-950' : 'text-ink-200 hover:bg-ink-800 hover:text-white'}`}>
          <Icon className="size-4" aria-hidden />{label}
        </NavLink>
      ))}
    </nav>
  )

  return (
    <div className="min-h-dvh bg-sand-50 lg:grid lg:grid-cols-[250px_1fr]">
      <aside className="hidden flex-col gap-6 bg-ink-900 p-4 lg:sticky lg:top-0 lg:flex lg:h-dvh">
        <Logo />
        {sidebar}
        <div className="mt-auto space-y-2 text-xs text-ink-400">
          <p>Signed in as <span className="font-semibold text-ink-100">{user?.firstName} {user?.lastName}</span></p>
          <div className="flex gap-2">
            <Link to="/" className="btn-ghost btn-sm text-ink-200 hover:bg-ink-800">Customer site</Link>
            <button className="btn-ghost btn-sm text-ink-200 hover:bg-ink-800" onClick={async () => { await signOut(); navigate('/') }}><LogOut className="size-3.5" />Sign out</button>
          </div>
        </div>
      </aside>
      <header className="sticky top-0 z-40 flex h-14 items-center justify-between bg-ink-900 px-4 lg:hidden">
        <Logo />
        <button className="btn-ghost text-white" aria-expanded={open} onClick={() => setOpen((o) => !o)}>{open ? <X className="size-5" /> : <Menu className="size-5" />}<span className="sr-only">Admin menu</span></button>
      </header>
      {open && <div className="bg-ink-900 px-4 pb-4 lg:hidden">{sidebar}</div>}
      <main id="main" className="min-w-0 px-4 py-6 sm:px-6 lg:px-8">
        <p className="mb-4 rounded-lg bg-amber-400/15 px-3 py-1.5 text-xs font-semibold text-amber-700">Demo environment — figures are computed from generated sample data, not a real business.</p>
        <Routes>
          <Route index element={<DashboardPage />} />
          <Route path="insights" element={<InsightsPage />} />
          <Route path="bookings" element={<BookingsAdminPage />} />
          <Route path="fleet" element={<FleetListPage />} />
          <Route path="fleet/new" element={<VehicleEditPage />} />
          <Route path="fleet/:id" element={<VehicleEditPage />} />
          <Route path="maintenance" element={<MaintenancePage />} />
          <Route path="customers" element={<CustomersPage />} />
          <Route path="reviews" element={<ReviewsAdminPage />} />
          <Route path="messages" element={<MessagesPage />} />
          <Route path="catalog" element={<CatalogPage />} />
          <Route path="audit" element={<AuditPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Routes>
      </main>
    </div>
  )
}
