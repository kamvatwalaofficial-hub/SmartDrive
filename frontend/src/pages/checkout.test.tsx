import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { api } from '../api/endpoints'
import { vehicle } from '../test/fixtures'
import { renderAt } from '../test/render'
import CheckoutPage from './CheckoutPage'

vi.mock('../api/endpoints', () => ({
  api: {
    auth: { me: vi.fn() },
    catalog: { vehicle: vi.fn(), locations: vi.fn() },
    bookings: { quote: vi.fn(), create: vi.fn() },
  },
}))

describe('Checkout', () => {
  it('enables reserving once the cancellation terms are accepted', async () => {
    vi.mocked(api.catalog.vehicle).mockResolvedValue({ summary: vehicle } as never)
    vi.mocked(api.catalog.locations).mockResolvedValue([{ id: 4, name: 'Cape Town City Centre', code: 'CTC', addressLine: '', city: 'Cape Town', province: 'Western Cape', isAirport: false }])
    vi.mocked(api.bookings.quote).mockResolvedValue({
      vehicleId: 'v1', vehicleName: 'Car', isAvailable: true, unavailableReasons: [], rentalDays: 3, dailyRate: 849, lines: [{ type: 'Rental', description: '3 days', amount: 2547 }],
      subtotal: 2547, discount: 0, total: 2547, vatIncluded: 332.22, deposit: 5000, pickupAt: '2030-10-24T09:00:00+02:00', returnAt: '2030-10-27T09:00:00+02:00',
      pickupLocation: 'Cape Town City Centre', returnLocation: 'Cape Town City Centre', currency: 'ZAR',
    })
    renderAt(<CheckoutPage />, { path: '/checkout', route: '/checkout?pickup=2030-10-24T09:00&return=2030-10-27T09:00&from=4&to=4&vehicle=v1' })
    const button = await screen.findByRole('button', { name: /Reserve and continue/ })
    expect(button).toBeDisabled()
    await userEvent.click(screen.getByRole('checkbox'))
    expect(screen.getByRole('checkbox')).toBeChecked()
    expect(screen.getByRole('button', { name: /Reserve and continue/ })).toBeEnabled()
  })
})
