import { Car, LayoutDashboard, LogOut, Menu, Sparkles, User, X } from 'lucide-react'
import { Suspense, useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router'
import { useAuth } from '../auth/AuthContext'
import { Spinner } from './ui'

const navLink = ({ isActive }: { isActive: boolean }) =>
  `rounded-lg px-3 py-2 text-sm font-semibold transition ${isActive ? 'text-amber-400' : 'text-ink-100 hover:text-white'}`

export function Logo({ light = true }: { light?: boolean }) {
  return (
    <Link to="/" className="flex items-center gap-2" aria-label="SmartDrive home">
      <span className="grid size-9 place-items-center rounded-xl bg-amber-500 text-ink-950"><Car className="size-5" aria-hidden /></span>
      <span className={`text-lg font-extrabold tracking-tight ${light ? 'text-white' : 'text-ink-900'}`}>Smart<span className="text-amber-500">Drive</span></span>
    </Link>
  )
}

export function SiteLayout() {
  const { user, isAdmin, signOut } = useAuth()
  const [open, setOpen] = useState(false)
  const location = useLocation()
  const navigate = useNavigate()

  useEffect(() => setOpen(false), [location.pathname])

  const links = (
    <>
      <NavLink to="/vehicles" className={navLink}>Browse cars</NavLink>
      <NavLink to="/recommend" className={navLink}><span className="inline-flex items-center gap-1"><Sparkles className="size-4" aria-hidden />Find my car</span></NavLink>
      {user && <NavLink to="/account/bookings" className={navLink}>My bookings</NavLink>}
      {isAdmin && <NavLink to="/admin" className={navLink}><span className="inline-flex items-center gap-1"><LayoutDashboard className="size-4" aria-hidden />Admin</span></NavLink>}
    </>
  )

  return (
    <div className="flex min-h-dvh flex-col">
      <a href="#main" className="sr-only focus:not-sr-only focus:absolute focus:left-4 focus:top-4 focus:z-50 focus:rounded-lg focus:bg-white focus:px-3 focus:py-2">Skip to content</a>
      <DemoBanner />
      <header className="sticky top-0 z-40 bg-ink-900/95 backdrop-blur">
        <div className="container-page flex h-16 items-center justify-between gap-4">
          <Logo />
          <nav className="hidden items-center gap-1 md:flex" aria-label="Main">{links}</nav>
          <div className="hidden items-center gap-2 md:flex">
            {user ? (
              <>
                <Link to="/account" className="btn-ghost text-ink-100 hover:bg-ink-800"><User className="size-4" aria-hidden />{user.firstName}</Link>
                <button className="btn-ghost text-ink-100 hover:bg-ink-800" onClick={async () => { await signOut(); navigate('/') }}>
                  <LogOut className="size-4" aria-hidden /><span className="sr-only">Sign out</span>
                </button>
              </>
            ) : (
              <>
                <Link to="/login" className="btn-ghost text-ink-100 hover:bg-ink-800">Sign in</Link>
                <Link to="/register" className="btn-primary">Create account</Link>
              </>
            )}
          </div>
          <button className="btn-ghost text-white hover:bg-ink-800 md:hidden" aria-expanded={open} aria-controls="mobile-nav" onClick={() => setOpen((o) => !o)}>
            {open ? <X className="size-5" /> : <Menu className="size-5" />}<span className="sr-only">Menu</span>
          </button>
        </div>
        {open && (
          <nav id="mobile-nav" aria-label="Mobile" className="container-page flex flex-col gap-1 border-t border-ink-800 pb-4 pt-2 md:hidden">
            {links}
            {user ? (
              <>
                <NavLink to="/account" className={navLink}>My account</NavLink>
                <button className="rounded-lg px-3 py-2 text-left text-sm font-semibold text-ink-100" onClick={async () => { await signOut(); navigate('/') }}>Sign out</button>
              </>
            ) : (
              <div className="mt-2 flex gap-2">
                <Link to="/login" className="btn-outline flex-1">Sign in</Link>
                <Link to="/register" className="btn-primary flex-1">Create account</Link>
              </div>
            )}
          </nav>
        )}
      </header>
      <main id="main" className="flex-1"><Suspense fallback={<Spinner />}><Outlet /></Suspense></main>
      <Footer />
    </div>
  )
}

function DemoBanner() {
  return (
    <div className="bg-amber-500 px-4 py-1.5 text-center text-xs font-semibold text-ink-950">
      Portfolio demo — vehicles, customers, reviews and payments are simulated sample data. No real bookings or charges.
    </div>
  )
}

function Footer() {
  return (
    <footer className="mt-16 bg-ink-950 text-ink-200">
      <div className="container-page grid gap-8 py-10 sm:grid-cols-3">
        <div>
          <Logo />
          <p className="mt-3 max-w-xs text-sm text-ink-400">A car-rental management system with explainable recommendations and fleet analytics. Built by Kamva Twala.</p>
        </div>
        <div className="text-sm">
          <p className="font-bold text-white">Explore</p>
          <ul className="mt-2 space-y-1.5">
            <li><Link to="/vehicles" className="hover:text-white">Browse cars</Link></li>
            <li><Link to="/recommend" className="hover:text-white">Find my car</Link></li>
            <li><Link to="/contact" className="hover:text-white">Contact</Link></li>
          </ul>
        </div>
        <div className="text-sm text-ink-400">
          <p className="font-bold text-white">About this demo</p>
          <p className="mt-2">Prices in South African Rand (VAT included). Times in South African Standard Time (SAST, UTC+2). Payments are simulated; vehicle images are illustrations.</p>
        </div>
      </div>
    </footer>
  )
}
