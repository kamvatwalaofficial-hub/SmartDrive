import type { BookingDetail, RecommendationResponse, VehicleSummary } from '../api/types'

export const vehicle: VehicleSummary = {
  id: 'v1', make: 'Toyota', model: 'Corolla Cross', year: 2024, displayName: '2024 Toyota Corolla Cross', category: 'SUV', categorySlug: 'suv',
  seats: 5, doors: 5, luggageCapacity: 3, transmission: 'Automatic', fuelType: 'Hybrid', dailyRate: 780, imageUrl: '/media/sample/x/side.svg', imageAlt: 'Side view',
  averageRating: 4.4, reviewCount: 12, currentState: 'Available', availableForPeriod: true, has3DModel: false, isSampleData: true,
}

export const recommendation: RecommendationResponse = {
  matches: [{
    vehicle, score: 87, components: [{ name: 'Budget fit', weight: 25, score: 0.9 }, { name: 'Space', weight: 10, score: 0.8 }],
    reasons: ['Seats 5 — room for your 4 passengers'], tradeOffs: ['Higher daily rate than the cheapest option'], summary: 'A strong all-round fit.',
    isAvailable: true, nextAvailableFrom: null, rentalDays: 5, estimatedTotal: 3900, effectiveDailyRate: 780, isAlternative: false,
  }],
  alternatives: [], unmetRequirements: [], suggestions: [], fleetSize: 21, eligibleCount: 6, algorithm: 'Weighted score.',
}

export function booking(overrides: Partial<BookingDetail['summary']> = {}): BookingDetail {
  return {
    summary: {
      id: 'b1', reference: 'SD-ABC123', vehicleId: 'v1', vehicleName: '2024 Toyota Corolla Cross', vehicleImageUrl: null,
      pickupAt: '2030-05-01T09:00:00+02:00', returnAt: '2030-05-04T09:00:00+02:00', pickupLocation: 'OR Tambo', returnLocation: 'OR Tambo',
      rentalDays: 3, totalAmount: 2340, status: 'Pending', paymentStatus: 'Unpaid', holdExpiresAt: new Date(Date.now() + 20 * 60_000).toISOString(),
      canCancel: true, canPay: true, canReview: false, hasReview: false, createdAt: '2030-04-01T09:00:00+02:00', customerName: null, customerEmail: null, isSampleData: false,
      ...overrides,
    },
    dailyRate: 780, lines: [{ type: 'Rental', description: '3 days × R780', amount: 2340 }], discountAmount: 0, vatAmount: 305.22, depositAmount: 2500,
    cancellationFee: 0, cancellationReason: null, pickupOdometerKm: null, returnOdometerKm: null,
    statusHistory: [{ from: null, to: 'Pending', at: '2030-04-01T09:00:00+02:00', reason: null }], payments: [],
    cancellation: { canCancel: true, fee: 0, refund: 0, explanation: 'Free cancellation until 48 hours before pickup.' }, allowedTransitions: [],
  }
}
