// TypeScript mirrors of the API contracts (backend/src/SmartDrive.Application/Contracts).
// Dates arrive as ISO-8601 strings with the South African offset (+02:00).

export type Iso = string

export interface PagedResult<T> {
  items: T[]
  page: number
  pageSize: number
  totalCount: number
  totalPages: number
}

export interface Problem {
  title?: string
  detail?: string
  status?: number
  code?: string
  traceId?: string
  errors?: Record<string, string[]>
}

// ------------------------------------------------------------------ catalogue
export interface Category { id: number; name: string; slug: string; description: string; vehicleCount: number }
export interface Location { id: number; name: string; code: string; addressLine: string; city: string; province: string; isAirport: boolean }
export interface Media { id: string; kind: string; url: string; altText: string; isPrimary: boolean; sortOrder: number; attribution: string | null; isSampleData: boolean }

export interface RatingSummary {
  average: number | null
  count: number
  distribution: Record<string, number>
  condition: number | null
  cleanliness: number | null
  comfort: number | null
  value: number | null
}

export interface VehicleSummary {
  id: string
  make: string
  model: string
  year: number
  displayName: string
  category: string
  categorySlug: string
  seats: number
  doors: number
  luggageCapacity: number
  transmission: string
  fuelType: string
  dailyRate: number
  imageUrl: string | null
  imageAlt: string | null
  averageRating: number | null
  reviewCount: number
  currentState: string
  availableForPeriod: boolean | null
  has3DModel: boolean
  isSampleData: boolean
}

export interface VehicleDetail {
  summary: VehicleSummary
  description: string
  features: string[]
  fuelEconomyLitresPer100Km: number | null
  comfortRating: number
  dailyMileageLimitKm: number | null
  excessMileageRatePerKm: number | null
  depositAmount: number
  media: Media[]
  videoUrl: string | null
  modelAssetUrl: string | null
  modelHasInterior: boolean
  modelAttribution: string | null
  ratings: RatingSummary
}

export interface VehicleSearch {
  q?: string
  category?: string
  transmission?: string
  fuel?: string
  minSeats?: number
  minLuggage?: number
  maxDailyRate?: number
  pickupAt?: Iso
  returnAt?: Iso
  availableOnly?: boolean
  sort?: string
  page?: number
  pageSize?: number
}

export interface Availability { isAvailable: boolean; reasons: string[]; nextAvailableFrom: Iso | null }

// ------------------------------------------------------------------ pricing and bookings
export interface QuoteRequest { vehicleId: string; pickupAt: Iso; returnAt: Iso; pickupLocationId: number; returnLocationId: number }
export interface PriceLine { type: string; description: string; amount: number }

export interface Quote {
  vehicleId: string
  vehicleName: string
  isAvailable: boolean
  unavailableReasons: string[]
  rentalDays: number
  dailyRate: number
  lines: PriceLine[]
  subtotal: number
  discount: number
  total: number
  vatIncluded: number
  deposit: number
  pickupAt: Iso
  returnAt: Iso
  pickupLocation: string
  returnLocation: string
  currency: string
}

export type BookingStatus = 'Pending' | 'Confirmed' | 'Active' | 'Completed' | 'Cancelled' | 'Rejected'

export interface BookingSummary {
  id: string
  reference: string
  vehicleId: string
  vehicleName: string
  vehicleImageUrl: string | null
  pickupAt: Iso
  returnAt: Iso
  pickupLocation: string
  returnLocation: string
  rentalDays: number
  totalAmount: number
  status: BookingStatus
  paymentStatus: string
  holdExpiresAt: Iso | null
  canCancel: boolean
  canPay: boolean
  canReview: boolean
  hasReview: boolean
  createdAt: Iso
  customerName: string | null
  customerEmail: string | null
  isSampleData: boolean
}

export interface StatusChange { from: string | null; to: string; at: Iso; reason: string | null }
export interface Payment { id: string; kind: string; amount: number; currency: string; provider: string; reference: string; status: string; isSimulated: boolean; createdAt: Iso }
export interface CancellationPreview { canCancel: boolean; fee: number; refund: number; explanation: string }

export interface BookingDetail {
  summary: BookingSummary
  dailyRate: number
  lines: PriceLine[]
  discountAmount: number
  vatAmount: number
  depositAmount: number
  cancellationFee: number
  cancellationReason: string | null
  pickupOdometerKm: number | null
  returnOdometerKm: number | null
  statusHistory: StatusChange[]
  payments: Payment[]
  cancellation: CancellationPreview
  allowedTransitions: string[]
}

// ------------------------------------------------------------------ reviews
export interface Review {
  id: string
  vehicleId: string
  vehicleName: string
  authorName: string
  overall: number
  condition: number
  cleanliness: number
  comfort: number
  value: number
  title: string
  comment: string
  createdAt: Iso
  updatedAt: Iso | null
  isMine: boolean
  canEdit: boolean
  isSampleData: boolean
}

export interface ReviewInput { overall: number; condition: number; cleanliness: number; comfort: number; value: number; title: string; comment: string }

export interface AdminReview {
  review: Review
  status: string
  customerName: string
  bookingReference: string
  reportCount: number
  reportReasons: string[]
  sentimentLabel: string | null
  sentimentScore: number | null
  positiveThemes: string[]
  negativeThemes: string[]
  moderationNote: string | null
}

// ------------------------------------------------------------------ recommendations
export interface RecommendationRequest {
  pickupAt: Iso
  returnAt: Iso
  pickupLocationId?: number | null
  returnLocationId?: number | null
  passengers?: number | null
  luggageBags?: number | null
  maxDailyBudget?: number | null
  maxTotalBudget?: number | null
  preferredCategory?: string | null
  transmission?: string | null
  transmissionRequired?: boolean
  fuel?: string | null
  journey?: string | null
  prioritiseComfort?: boolean
  prioritiseFuelEfficiency?: boolean
  prioritiseSpace?: boolean
}

export interface ScoreComponent { name: string; weight: number; score: number }

export interface RecommendedVehicle {
  vehicle: VehicleSummary
  score: number
  components: ScoreComponent[]
  reasons: string[]
  tradeOffs: string[]
  summary: string
  isAvailable: boolean
  nextAvailableFrom: Iso | null
  rentalDays: number
  estimatedTotal: number
  effectiveDailyRate: number
  isAlternative: boolean
}

export interface RecommendationResponse {
  matches: RecommendedVehicle[]
  alternatives: RecommendedVehicle[]
  unmetRequirements: string[]
  suggestions: string[]
  fleetSize: number
  eligibleCount: number
  algorithm: string
}

export interface InterpretedPreferences {
  passengers: number | null
  luggageBags: number | null
  durationDays: number | null
  maxDailyBudget: number | null
  maxTotalBudget: number | null
  preferredCategory: string | null
  transmission: string | null
  transmissionRequired: boolean
  fuel: string | null
  journey: string | null
  prioritiseComfort: boolean
  prioritiseFuelEfficiency: boolean
  prioritiseSpace: boolean
  understood: string[]
  notes: string[]
  source: string
}

// ------------------------------------------------------------------ accounts
export interface User { id: string; email: string; firstName: string; lastName: string; phoneNumber: string | null; roles: string[] }
export interface AuthResponse { token: string; expiresAt: Iso; user: User }
export interface Profile {
  user: User
  addressLine: string | null
  suburb: string | null
  city: string | null
  province: string | null
  postalCode: string | null
  dateOfBirth: string | null
  driverLicenceExpiry: string | null
}
export interface UpdateProfile {
  firstName: string
  lastName: string
  phoneNumber: string | null
  addressLine: string | null
  suburb: string | null
  city: string | null
  province: string | null
  postalCode: string | null
  dateOfBirth: string | null
  driverLicenceExpiry: string | null
}
export interface Notification { id: string; title: string; message: string; link: string | null; isRead: boolean; createdAt: Iso }

// ------------------------------------------------------------------ administration
export interface UpsertVehicle {
  registrationNumber: string
  make: string
  model: string
  year: number
  categoryId: number
  description: string
  seats: number
  doors: number
  luggageCapacity: number
  transmission: string
  fuelType: string
  fuelEconomyLitresPer100Km: number | null
  comfortRating: number
  features: string[]
  dailyRate: number
  dailyMileageLimitKm: number | null
  excessMileageRatePerKm: number | null
  depositAmount: number
  odometerKm: number
  serviceIntervalKm: number
  nextServiceDueKm: number | null
  nextServiceDueDate: Iso | null
  videoUrl: string | null
}

export interface AdminVehicle {
  summary: VehicleSummary
  registrationNumber: string
  status: string
  odometerKm: number
  serviceIntervalKm: number
  nextServiceDueKm: number | null
  nextServiceDueDate: Iso | null
  serviceState: string
  upcomingBookings: number
  updatedAt: Iso
  archivedAt: Iso | null
}

export interface Maintenance {
  id: string
  vehicleId: string
  vehicleName: string
  registrationNumber: string
  type: string
  status: string
  title: string
  description: string | null
  startAt: Iso
  endAt: Iso | null
  odometerKm: number | null
  cost: number | null
  isReportedIssue: boolean
  isSampleData: boolean
}

export interface UpsertMaintenance {
  vehicleId: string
  type: string
  title: string
  description: string | null
  startAt: Iso
  endAt: Iso | null
  odometerKm: number | null
  cost: number | null
  isReportedIssue: boolean
}

export interface MaintenanceAlert {
  vehicleId: string
  vehicleName: string
  registrationNumber: string
  serviceState: string
  odometerKm: number
  nextServiceDueKm: number | null
  nextServiceDueDate: Iso | null
  kmRemaining: number | null
  daysRemaining: number | null
  hasRepeatedIssues: boolean
  recentIssueCount: number
}

export interface VehicleHistory {
  bookings: BookingSummary[]
  maintenance: Maintenance[]
  mileage: { recordedAt: Iso; odometerKm: number; source: string }[]
}

export interface AdminCustomer {
  id: string
  fullName: string
  email: string
  phoneNumber: string | null
  isActive: boolean
  roles: string[]
  bookingCount: number
  totalSpend: number
  createdAt: Iso
  lastLoginAt: Iso | null
  isSampleData: boolean
}

export interface AuditEntry { id: number; userEmail: string | null; action: string; entityType: string; entityId: string; details: string | null; createdAt: Iso }
export interface AdminLocation extends Location { isActive: boolean }

// ------------------------------------------------------------------ analytics
export interface Period { from: string; to: string; category: string | null; label: string }
export interface CountPoint { label: string; value: number }
export interface SeriesPoint { label: string; values: Record<string, number> }

export interface Dashboard {
  totalVehicles: number
  availableNow: number
  rentedNow: number
  reservedNow: number
  inMaintenance: number
  activeRentals: number
  pendingBookings: number
  upcomingPickups: number
  upcomingReturns: number
  maintenanceAlerts: number
  revenueThisMonth: number
  revenueLastMonth: number
  fleetUtilisationLast30Days: number
  averageRating: number | null
  reviewCount: number
  reportedReviews: number
  weeklyBookings: CountPoint[]
  nextPickups: BookingSummary[]
  nextReturns: BookingSummary[]
  containsSampleData: boolean
}

export interface Demand {
  period: Period
  totalBookings: number
  averageRentalDays: number
  weeklyByCategory: SeriesPoint[]
  byCategory: CountPoint[]
  pickupsByWeekday: CountPoint[]
  monthly: CountPoint[]
  weekdayPickups: number
  weekendPickups: number
  definition: string
}

export interface VehicleUtilisation { vehicleId: string; name: string; category: string; fleetDays: number; maintenanceDays: number; availableDays: number; bookedDays: number; utilisation: number }
export interface Utilisation {
  period: Period
  fleetUtilisation: number
  categories: { category: string; vehicles: number; bookedDays: number; availableDays: number; utilisation: number }[]
  vehicles: VehicleUtilisation[]
  definition: string
}

export interface Revenue {
  period: Period
  realisedRevenue: number
  bookedRevenue: number
  cancellationFees: number
  discountsGiven: number
  averageRentalValue: number
  cancellationRate: number
  bookings: number
  cancellations: number
  monthly: SeriesPoint[]
  byCategory: CountPoint[]
  topVehicles: { vehicleId: string; vehicle: string; category: string; revenue: number; rentals: number; revenuePerRental: number }[]
  definition: string
}

export interface SentimentExample { reviewId: string; vehicle: string; overall: number; title: string; excerpt: string; label: string; themes: string[]; createdAt: Iso }
export interface Sentiment {
  period: Period
  reviewsAnalysed: number
  labels: CountPoint[]
  themes: { theme: string; positive: number; negative: number }[]
  negativeExamples: SentimentExample[]
  positiveExamples: SentimentExample[]
  method: string
  disclaimer: string
}

export interface Forecast {
  category: string | null
  history: CountPoint[]
  forecast: CountPoint[]
  result: {
    hasEnoughData: boolean
    historyWeeks: number
    forecast: number[]
    chosenMethod: string
    evaluations: { method: string; meanAbsoluteError: number }[]
    alpha: number | null
    beta: number | null
    note: string
  }
  disclaimer: string
}

export interface Insight {
  area: string
  severity: string
  title: string
  finding: string
  period: string
  evidence: string
  suggestedAction: string
  confidence: string
  limitations: string
}

export interface Insights { period: Period; insights: Insight[]; totalBookings: number; notice: string }

export interface ContactMessage { id: string; name: string; email: string; subject: string; message: string; isHandled: boolean; createdAt: Iso }
