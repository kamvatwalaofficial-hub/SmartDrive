import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ApiError } from '../api/client'
import { api } from '../api/endpoints'
import { RequireAuth } from '../components/Guards'
import { VehicleCard } from '../components/VehicleCard'
import { booking, recommendation, vehicle } from '../test/fixtures'
import { renderAt } from '../test/render'
import { LoginPage } from './AuthPages'
import BookingPage from './BookingPage'
import RecommendPage from './RecommendPage'

vi.mock('../api/endpoints', () => ({
  api: {
    auth: { login: vi.fn(), me: vi.fn(), logout: vi.fn() },
    catalog: { locations: vi.fn().mockResolvedValue([]) },
    bookings: { detail: vi.fn(), pay: vi.fn(), cancellation: vi.fn() },
    recommendations: { recommend: vi.fn(), interpret: vi.fn() },
  },
}))

beforeEach(() => {
  vi.clearAllMocks()
  localStorage.clear()
  vi.mocked(api.catalog.locations).mockResolvedValue([])
})

describe('VehicleCard', () => {
  it('shows price, availability for the chosen dates and the sample-data label', () => {
    renderAt(<VehicleCard vehicle={vehicle} />)
    expect(screen.getByText('2024 Toyota Corolla Cross')).toBeInTheDocument()
    expect(screen.getByText('Available for your dates')).toBeInTheDocument()
    expect(screen.getByText('Sample data')).toBeInTheDocument()
  })
})

describe('Login', () => {
  it('validates before calling the API', async () => {
    renderAt(<LoginPage />)
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument()
    expect(api.auth.login).not.toHaveBeenCalled()
  })

  it('shows the server message on failure', async () => {
    vi.mocked(api.auth.login).mockRejectedValue(new ApiError(400, { detail: 'The email address or password is incorrect.' }, 'x'))
    renderAt(<LoginPage />)
    await userEvent.type(screen.getByLabelText('Email'), 'someone@example.com')
    await userEvent.type(screen.getByLabelText('Password'), 'Wrong-pass-1')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByRole('alert')).toHaveTextContent('incorrect')
  })

  it('stores the token and leaves the page on success', async () => {
    vi.mocked(api.auth.login).mockResolvedValue({ token: 't', expiresAt: '', user: { id: 'u', email: 'a@b.co', firstName: 'A', lastName: 'B', phoneNumber: null, roles: ['Customer'] } })
    renderAt(<LoginPage />, { path: '/login', route: '/login?next=/account' })
    await userEvent.type(screen.getByLabelText('Email'), 'a@b.co')
    await userEvent.type(screen.getByLabelText('Password'), 'Valid-Pass-1')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))
    expect(await screen.findByText('Navigated away')).toBeInTheDocument()
    expect(localStorage.getItem('smartdrive.token')).toBe('t')
  })
})

describe('RequireAuth', () => {
  it('sends anonymous visitors to sign in', () => {
    renderAt(<RequireAuth><p>Secret</p></RequireAuth>, { path: '/account', route: '/account' })
    expect(screen.queryByText('Secret')).not.toBeInTheDocument()
    expect(screen.getByText('Navigated away')).toBeInTheDocument()
  })
})

describe('Recommendation wizard', () => {
  it('shows what was understood from free text, then explained results', async () => {
    vi.mocked(api.recommendations.interpret).mockResolvedValue({
      passengers: 4, luggageBags: null, durationDays: 5, maxDailyBudget: 600, maxTotalBudget: null, preferredCategory: null, transmission: null, transmissionRequired: false,
      fuel: null, journey: null, prioritiseComfort: true, prioritiseFuelEfficiency: false, prioritiseSpace: false,
      understood: ['4 passengers', 'Budget R600 per day'], notes: [], source: 'rules',
    })
    vi.mocked(api.recommendations.recommend).mockResolvedValue(recommendation)
    renderAt(<RecommendPage />)

    await userEvent.type(screen.getByLabelText('Trip description'), 'four adults, five days, R600 a day')
    await userEvent.click(screen.getByRole('button', { name: 'Interpret' }))
    expect(await screen.findByText('Budget R600 per day')).toBeInTheDocument()
    expect(screen.getByText(/rule-based parser/)).toBeInTheDocument()

    await userEvent.click(screen.getByRole('button', { name: /Use these preferences/ }))
    await userEvent.click(screen.getByRole('button', { name: /Next/ }))
    expect(screen.getByText('4')).toBeInTheDocument()
    expect(screen.getByLabelText('Max. per day (R, optional)')).toHaveValue(600)
    await userEvent.click(screen.getByRole('button', { name: /Recommend cars/ }))

    expect(await screen.findByText('Seats 5 — room for your 4 passengers')).toBeInTheDocument()
    expect(screen.getByText('Higher daily rate than the cheapest option')).toBeInTheDocument()
    expect(screen.getByLabelText('Score breakdown')).toHaveTextContent('Budget')
    const body = vi.mocked(api.recommendations.recommend).mock.calls[0][0]
    expect(body).toMatchObject({ passengers: 4, maxDailyBudget: 600, prioritiseComfort: true })
    expect(body.pickupAt).toMatch(/\+02:00$/)
  })
})

describe('Booking payment', () => {
  it('labels the payment as simulated and retries a network failure with the same idempotency key', async () => {
    vi.mocked(api.bookings.detail).mockResolvedValue(booking())
    vi.mocked(api.bookings.pay)
      .mockRejectedValueOnce(new ApiError(0, undefined, 'Could not reach SmartDrive.'))
      .mockResolvedValueOnce(booking({ status: 'Confirmed', paymentStatus: 'Paid', canPay: false }))
    renderAt(<BookingPage />, { path: '/bookings/:reference', route: '/bookings/SD-ABC123' })

    expect(await screen.findByText(/Simulated payment\./)).toBeInTheDocument()
    const pay = screen.getByRole('button', { name: /Pay .* \(simulated\)/ })
    await userEvent.click(pay)
    expect(await screen.findByText('Could not reach SmartDrive.')).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: /Pay .* \(simulated\)/ }))

    await waitFor(() => expect(api.bookings.pay).toHaveBeenCalledTimes(2))
    const [first, second] = vi.mocked(api.bookings.pay).mock.calls
    expect(second[1]).toBe(first[1])
    expect(await screen.findAllByText('Confirmed')).not.toHaveLength(0)
  })
})
