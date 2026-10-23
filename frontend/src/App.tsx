import { lazy, Suspense } from 'react'
import { Route, Routes } from 'react-router'
import { RequireAuth } from './components/Guards'
import { SiteLayout } from './components/Layout'
import { Spinner } from './components/ui'
import HomePage from './pages/HomePage'
import BrowsePage from './pages/BrowsePage'
import NotFoundPage from './pages/NotFoundPage'

const VehiclePage = lazy(() => import('./pages/VehiclePage'))
const RecommendPage = lazy(() => import('./pages/RecommendPage'))
const CheckoutPage = lazy(() => import('./pages/CheckoutPage'))
const BookingPage = lazy(() => import('./pages/BookingPage'))
const MyBookingsPage = lazy(() => import('./pages/MyBookingsPage'))
const AccountPage = lazy(() => import('./pages/AccountPage'))
const ContactPage = lazy(() => import('./pages/ContactPage'))
const LoginPage = lazy(() => import('./pages/AuthPages').then((m) => ({ default: m.LoginPage })))
const RegisterPage = lazy(() => import('./pages/AuthPages').then((m) => ({ default: m.RegisterPage })))
// The admin area (and its chart library) loads only for administrators.
const AdminApp = lazy(() => import('./pages/admin/AdminApp'))

export default function App() {
  return (
    <Suspense fallback={<Spinner />}>
    <Routes>
      <Route element={<SiteLayout />}>
        <Route index element={<HomePage />} />
        <Route path="vehicles" element={<BrowsePage />} />
        <Route path="vehicles/:id" element={<VehiclePage />} />
        <Route path="recommend" element={<RecommendPage />} />
        <Route path="login" element={<LoginPage />} />
        <Route path="register" element={<RegisterPage />} />
        <Route path="contact" element={<ContactPage />} />
        <Route path="checkout" element={<RequireAuth><CheckoutPage /></RequireAuth>} />
        <Route path="bookings/:reference" element={<RequireAuth><BookingPage /></RequireAuth>} />
        <Route path="account/bookings" element={<RequireAuth><MyBookingsPage /></RequireAuth>} />
        <Route path="account" element={<RequireAuth><AccountPage /></RequireAuth>} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
      <Route path="admin/*" element={
        <RequireAuth admin>
          <Suspense fallback={<Spinner label="Loading the admin dashboard…" />}><AdminApp /></Suspense>
        </RequireAuth>
      } />
    </Routes>
    </Suspense>
  )
}
